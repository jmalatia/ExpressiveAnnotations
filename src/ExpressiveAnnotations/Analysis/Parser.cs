﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace ExpressiveAnnotations.Analysis
{
    /* EBNF GRAMMAR:
     * 
     * expression => or-exp
     * or-exp     => and-exp [ "||" or-exp ]
     * and-exp    => not-exp [ "&&" and-exp ]
     * not-exp    => [ "!" ] rel-exp
     * rel-exp    => val [ rel-op val ]
     * rel-op     => "==" | "!=" | ">" | ">=" | "<" | "<="
     * val        => "null" | int | float | bool | string | func | "(" or-exp ")"
     */

    /// <summary>
    /// Performs syntactic analysis of provided logical expression within given context.
    /// </summary>
    public sealed class Parser
    {
        private Stack<Token> Tokens { get; set; }
        private IDictionary<string, Type> Enums { get; set; }
        private IDictionary<string, Type> Members { get; set; }
        private Type ContextType { get; set; }
        private Expression ContextExpression { get; set; }
        private IDictionary<string, LambdaExpression> Functions { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Parser" /> class.
        /// </summary>
        public Parser()
        {
            Enums = new Dictionary<string, Type>();
            Members = new Dictionary<string, Type>();
            Functions = new Dictionary<string, LambdaExpression>();            
        }

        /// <summary>
        /// Parses the specified logical expression into expression tree within given object context.
        /// </summary>
        /// <typeparam name="Context">The type identifier of the context within which the expression is interpreted.</typeparam>
        /// <param name="expression">The logical expression.</param>
        /// <returns>A delegate containing the compiled version of the lambda expression described by created expression tree.</returns>
        public Func<Context, bool> Parse<Context>(string expression)
        {
            ContextType = typeof(Context);
            var param = Expression.Parameter(typeof(Context));
            ContextExpression = param;
            InitTokenizer(expression);
            var expressionTree = ParseExpression();
            var lambda = Expression.Lambda<Func<Context, bool>>(expressionTree, param);
            return lambda.Compile();
        }

        /// <summary>
        /// Parses the specified logical expression and builds expression tree.
        /// </summary>
        /// <param name="context">The type instance of the context within which the expression is interpreted.</param>
        /// <param name="expression">The logical expression.</param>
        /// <returns>A delegate containing the compiled version of the lambda expression described by produced expression tree.</returns>
        public Func<object, bool> Parse(Type context, string expression)
        {
            ContextType = context;
            var param = Expression.Parameter(typeof(object));
            ContextExpression = Expression.Convert(param, context);
            InitTokenizer(expression);
            var expressionTree = ParseExpression();
            var lambda = Expression.Lambda<Func<object, bool>>(expressionTree, param);
            return lambda.Compile();
        }

        /// <summary>
        /// Adds function signature to the parser context.
        /// </summary>
        /// <typeparam name="Result">Type identifier of returned result.</typeparam>
        /// <param name="name">Function name.</param>
        /// <param name="func">Function lambda.</param>
        public void AddFunction<Result>(string name, Expression<Func<Result>> func)
        {
            Functions.Add(name, func);
        }

        /// <summary>
        /// Adds function signature to the parser context.
        /// </summary>
        /// <typeparam name="Arg1">First argument.</typeparam>
        /// <typeparam name="Result">Type identifier of returned result.</typeparam>
        /// <param name="name">Function name.</param>
        /// <param name="func">Function lambda.</param>
        public void AddFunction<Arg1, Result>(string name, Expression<Func<Arg1, Result>> func)
        {
            Functions.Add(name, func);
        }

        /// <summary>
        /// Adds function signature to the parser context.
        /// </summary>
        /// <typeparam name="Arg1">First argument.</typeparam>
        /// <typeparam name="Arg2">Second argument.</typeparam>
        /// <typeparam name="Result">Type identifier of returned result.</typeparam>
        /// <param name="name">Function name.</param>
        /// <param name="func">Function lambda.</param>
        public void AddFunction<Arg1, Arg2, Result>(string name, Expression<Func<Arg1, Arg2, Result>> func)
        {
            Functions.Add(name, func);
        }

        /// <summary>
        /// Gets the parsed fields and properties.
        /// </summary>
        /// <returns>Fields and properties.</returns>
        public IDictionary<string, Type> GetMembers()
        {
            return Members;
        }

        /// <summary>
        /// Gets the parsed enums.
        /// </summary>
        /// <returns>Enums.</returns>
        public IDictionary<string, Type> GetEnums()
        {
            return Enums;
        }

        private void InitTokenizer(string expression)
        {
            var lexer = new Lexer();
            Tokens = new Stack<Token>(lexer.Analyze(expression).Reverse());
        }

        private TokenId PeekType()
        {
            return Tokens.Any() ? Tokens.Peek().Id : TokenId.NONE;
        }

        private object PeekValue()
        {
            return Tokens.Peek().Value;
        }

        private void ReadToken()
        {
            Tokens.Pop();
        }

        private Expression ParseExpression()
        {
            var expr = ParseOrExp();
            if (PeekType() != TokenId.NONE)
                throw new InvalidOperationException(string.Format("Unexpected token {0}.", PeekValue()));
            return expr;
        }

        private Expression ParseOrExp()
        {
            var arg1 = ParseAndExp();
            if (PeekType() != TokenId.OR)
                return arg1;
            ReadToken();
            var arg2 = ParseOrExp();
            return Expression.Or(arg1, arg2);
        }

        private Expression ParseAndExp()
        {
            var arg1 = ParseNotExp();
            if (PeekType() != TokenId.AND)
                return arg1;
            ReadToken();
            var arg2 = ParseAndExp();
            return Expression.And(arg1, arg2);
        }

        private Expression ParseNotExp()
        {
            if (PeekType() != TokenId.NOT)
                return ParseRelExp();
            ReadToken();
            return Expression.Not(ParseRelExp());
        }

        private Expression ParseRelExp()
        {
            var arg1 = ParseVal();
            if (!new[] {TokenId.LT, TokenId.LE, TokenId.GT, TokenId.GE, TokenId.EQ, TokenId.NEQ}.Contains(PeekType()))
                return arg1;
            var oper = PeekType();
            ReadToken();
            var arg2 = ParseVal();

            Helper.MakeTypesCompatible(arg1, arg2, out arg1, out arg2);
            switch (oper)
            {
                case TokenId.LT:
                    return Expression.LessThan(arg1, arg2);
                case TokenId.LE:
                    return Expression.LessThanOrEqual(arg1, arg2);
                case TokenId.GT:
                    return Expression.GreaterThan(arg1, arg2);
                case TokenId.GE:
                    return Expression.GreaterThanOrEqual(arg1, arg2);
                case TokenId.EQ:
                    return Expression.Equal(arg1, arg2);
                case TokenId.NEQ:
                    return Expression.NotEqual(arg1, arg2);
                default:
                    throw new InvalidOperationException("Unexpected operator.");
            }
        }

        private Expression ParseVal()
        {            
            if (PeekType() == TokenId.LEFT_BRACKET)
            {
                ReadToken();
                var arg = ParseOrExp();
                if (PeekType() != TokenId.RIGHT_BRACKET)
                    throw new InvalidOperationException(string.Format("Unexpected token {0}. Closing bracket missing.", PeekValue()));
                ReadToken();
                return arg;
            }
            
            switch (PeekType())
            {
                case TokenId.NULL:
                    return ParseNull();
                case TokenId.INT:
                    return ParseInt();
                case TokenId.FLOAT:
                    return ParseFloat();
                case TokenId.BOOL:
                    return ParseBool();
                case TokenId.STRING:
                    return ParseString();
                case TokenId.FUNC:
                    return ParseFunc();
                default:
                    throw new InvalidOperationException(string.Format("Unexpected token {0}. Available values: null, int, float, bool, string or func.", PeekValue()));
            }
        }

        private Expression ParseNull()
        {
            ReadToken();
            return Expression.Constant(null);
        }

        private Expression ParseInt()
        {
            var value = PeekValue();
            ReadToken();
            return Expression.Constant(value, typeof(int));            
        }

        private Expression ParseFloat()
        {
            var value = PeekValue();
            ReadToken();
            return Expression.Constant(value, typeof(float));
        }

        private Expression ParseBool()
        {
            var value = PeekValue();
            ReadToken();
            return Expression.Constant(value, typeof(bool));
        }

        private Expression ParseString()
        {
            var value = PeekValue();
            ReadToken();
            return Expression.Constant(value, typeof(string));
        }

        private Expression ParseFunc()
        {
            var name = PeekValue().ToString();
            ReadToken(); // read name

            if (PeekType() != TokenId.LEFT_BRACKET)
                return ExtractMemberExpression(name);

            ReadToken(); // read "("

            // read arguments
            var args = new List<Expression>();
            while (PeekType() != TokenId.RIGHT_BRACKET)
            {
                var arg = ParseOrExp();
                if (PeekType() == TokenId.COMMA)
                    ReadToken();
                args.Add(arg);
            }

            if (PeekType() != TokenId.RIGHT_BRACKET)
                throw new InvalidOperationException(string.Format("Unexpected token {0}. Function invocation does not contain closing bracket.", PeekValue()));
            ReadToken();
            
            var mi = ContextType.GetMethod(name); // check if custom func is defined for model
            if (mi != null)
                return Expression.Call(ContextExpression, mi, args);

            if(!Functions.ContainsKey(name))
                throw new InvalidOperationException(string.Format("Function {0} not found.", name));

            return CreateLambdaCallExpression(Functions[name], args, name);
        }

        private Expression ExtractMemberExpression(string name)
        {
            var expr = ExtractMemberExpression(name, ContextType, ContextExpression);
            if (expr != null)
            {
                if(!Members.ContainsKey(name))
                    Members.Add(name, expr.Type);
                return expr;
            }

            var parts = name.Split('.');
            if (parts.Count() > 1)
            {
                name = string.Join(".", parts.Take(parts.Count() - 1).ToArray());
                var enumTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.GetTypes()).Where(t => t.IsEnum && t.FullName.EndsWith(name)).ToList();

                if (enumTypes.Count() > 1)
                    throw new ArgumentException(string.Format("Dynamic extraction failed. {0} enum identifier is ambigous. Provide detailed namespace.", name), name);

                var type = enumTypes.SingleOrDefault();
                if (type != null)
                {
                    if (!Enums.ContainsKey(name))
                        Enums.Add(name, type);
                    return ExtractMemberExpression(parts.Last(), type, Expression.Parameter(type));
                }
            }

            throw new ArgumentException(string.Format("Dynamic extraction failed. Member {0} not found.", name), name);
        }

        private Expression ExtractMemberExpression(string name, Type type, Expression expr)
        {            
            var parts = name.Split('.');
            foreach (var part in parts)
            {
                var pi = type.GetProperty(part);
                if (pi == null)
                {
                    var fi = type.GetField(part);
                    if (fi == null)                    
                        return null;
                   
                    expr = fi.IsStatic ? Expression.Field(null, fi) : Expression.Field(expr, fi);
                    type = fi.FieldType;
                    continue;
                }
                expr = Expression.Property(expr, pi);
                type = pi.PropertyType;
            }
            return expr;
        }

        private static InvocationExpression CreateLambdaCallExpression(LambdaExpression funcExpr, IList<Expression> parsedArgs, string funcName)
        {
            if (funcExpr.Parameters.Count != parsedArgs.Count)
                throw new InvalidOperationException(string.Format("Incorrect number of parameters provided. Function {0} expects {1}, not {2}.", funcName, funcExpr.Parameters.Count, parsedArgs.Count));

            var convertedArgs = new List<Expression>();
            for (var i = 0; i < parsedArgs.Count; i++)
            {
                var arg = parsedArgs[i];
                var param = funcExpr.Parameters[i];
                if (arg.Type == param.Type)
                    convertedArgs.Add(arg);
                else
                {
                    try
                    {
                        convertedArgs.Add(Expression.Convert(arg, param.Type));
                    }
                    catch
                    {
                        throw new InvalidOperationException(string.Format("Argument {0} type conversion from {1} to needed {2} failed.", i, arg.Type.Name, param.Type.Name));
                    }
                }
            }
            return Expression.Invoke(funcExpr, convertedArgs);
        }
    }
}
