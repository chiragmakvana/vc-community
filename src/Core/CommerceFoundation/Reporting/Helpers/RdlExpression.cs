﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace VirtoCommerce.Foundation.Reporting.Helpers
{
    public class RdlExpression
    {
        public class Functions
        {
            public static DateTime Now()
            {
                return DateTime.Now;
            }

            public static DateTime Today()
            {
                return DateTime.Today;
            }

            public static int Day(DateTime date)
            {
                return date.Day;
            }

            public static DateTime DateAdd(string interval, decimal value, DateTime dateTime)
            {
                return DateAdd(interval, (double)value, dateTime);
            }

            public static DateTime DateAdd(string interval, int value, DateTime dateTime)
            {
                return DateAdd(interval, (double)value, dateTime);
            }

            public static DateTime DateAdd(string interval, double value, DateTime dateTime)
            {
                switch (interval)
                {
                    case "d":
                        return dateTime.AddDays((double)value);
                    case "m":
                        return dateTime.AddMonths((int)value);
                    case "yyyy":
                        return dateTime.AddYears((int)value);
                }

                return dateTime;
            }

            public static string Join(string text1, string text2)
            {
                return Join(new[] { text1, text2 });
            }

            public static string Concat(string text1, string text2)
            {
                return Join(new[] { text1, text2 });
            }

            public static string Join(params string[] args)
            {
                StringBuilder sb = new StringBuilder();
                foreach (var s in args)
                {
                    sb.Append(s);
                }

                return sb.ToString();
            }
        }

        public enum GlobalField
        {
            ExecutionTime,
            Language,
            ReportFolder,
            ReportName,
            UserID
        }

        //public IDictionary<GlobalField, object> BuiltInGlobalFields = new Dictionary<GlobalField, object>();
        //public IDictionary<string, object> Parameters = new Dictionary<string, object>();
        //public DataSet DataSets = new DataSet();

        public delegate BinaryExpression OperationCreator(Expression firstOperand, Expression secondOperand);
        public delegate object ParameterRetriever(string paramName);
        public delegate object BuiltInValueRetriever(GlobalField fieldName);

        public ParameterRetriever GetParameterValue;
        public BuiltInValueRetriever GetGlobalValue;

        private string _expression;

        private enum EndingReason
        {
            EndOfText,
            ClosingBracket,
            ListSeparator,
            UnknownKeyword
        }

        private readonly IDictionary<string, EndingReason> EndingKeywords = new Dictionary<string, EndingReason>
        {
            {")", EndingReason.ClosingBracket},
            {",", EndingReason.ListSeparator}
        };

        private readonly IDictionary<string, OperationCreator> OperatorKeywords = new Dictionary<string, OperationCreator>
        {
            {"*", Expression.Multiply},
            {"/", Expression.Divide},
            {"+", Expression.Add},
            {"-", Expression.Subtract},
            {"&", (op1, op2) => Expression.Add(op1, op2, typeof(Functions).GetMethod("Concat"))}
        };

        private readonly IDictionary<ExpressionType, int> OperatorPrecedence = new Dictionary<ExpressionType, int>
        {
            {ExpressionType.Multiply, 1},
            {ExpressionType.Divide, 1},
            {ExpressionType.Add, 2},
            {ExpressionType.Subtract, 2},
            {ExpressionType.Default, 5}
        };

        public RdlExpression(string expression)
        {
            _expression = expression;
        }

        private Expression _definition;
        public Expression Compile()
        {
            return _definition ?? (_definition=ParseExpression(_expression));
        }

        public object Evaluate()
        {
            if (string.IsNullOrWhiteSpace(_expression) ||
                !_expression.TrimStart().StartsWith("="))
            {
                return _expression;
            }

            var func = Expression.Lambda(Compile()).Compile();
            var result = func.DynamicInvoke();
            var constantExpression = result as ConstantExpression;
            if (constantExpression != null)
            {
                return constantExpression.Value;
            }

            return result;
        }

        private Expression ParseExpression(string expression)
        {
            Expression result = Expression.Empty();
            
            expression = expression.Trim();
            if (expression.StartsWith("="))
            {
                string exprText = expression.Substring(1);
                if (Parse(ref result, ref exprText) != EndingReason.EndOfText)
                {
                    throw new RdlExpressionSyntaxException("Expression ended incorrectly.");
                }
            }
            else
            {
                result = Expression.Constant(expression);
            }

            return result;
        }

        private EndingReason Parse(ref Expression expr, ref string exprText)
        {
            exprText = exprText.TrimStart();
            var token = String.Empty;

            var endingReason = EndingReason.EndOfText;
            if (TokenIsEnding(ref exprText, ref endingReason))
            {
                return endingReason;
            }

            if ( !( expr is DefaultExpression) && TokenIsOperator(ref exprText, ref token) )
            {
                Expression secondExpression = Expression.Empty();
                endingReason = Parse(ref secondExpression, ref exprText);
                expr = GetOperatorExpression(expr, token, secondExpression);
                return endingReason;
            }
            
            if (TokenIsNumericConstant(ref exprText, ref token))
            {
                CheckAndAssign(ref expr, Expression.Constant(Decimal.Parse(token)));
                return Parse(ref expr, ref exprText);
            }
            
            if (TokenIsTextConstant(ref exprText, ref token))
            {
                CheckAndAssign(ref expr, Expression.Constant(token));
                return Parse(ref expr, ref exprText);
            }

            if (TokenIsGlobalField(ref exprText, ref token))
            {
                CheckAndAssign(ref expr, GetGlobalFieldExpression(token));
                return Parse(ref expr, ref exprText);
            }

            if (TokenIsParameter(ref exprText, ref token))
            {
                CheckAndAssign(ref expr, Expression.Constant(GetParameterExpression(token)));
                return Parse(ref expr, ref exprText);
            }

            if (TokenIsFunction(ref exprText, ref token))
            {
                CheckAndAssign(ref expr, GetFunctionExpression(ref exprText, token));  
                return Parse(ref expr, ref exprText);
            }

            return EndingReason.UnknownKeyword;
        }

        private void CheckAndAssign(ref Expression expr, Expression newExpr)
        {
            if (
                (expr.NodeType == ExpressionType.Default && newExpr.NodeType == ExpressionType.Constant) ||
                (expr.NodeType == ExpressionType.Default && newExpr.NodeType == ExpressionType.Lambda) ||
                (expr.NodeType == ExpressionType.Default && newExpr.NodeType == ExpressionType.MemberAccess) ||
                (expr.NodeType == ExpressionType.Default && newExpr.NodeType == ExpressionType.Call) 
               )
            {
                expr = newExpr;
            }
            else
            {
                throw new RdlExpressionSyntaxException();
            }
        }

        private const string rxNumeric = @"^(?<const>(\-)?[0-9]*(\.)?([0-9]*)?)";
        private bool TokenIsNumericConstant(ref string text, ref string token)
        {
            var mm = Regex.Match(text, rxNumeric);
            var result = mm.Success && !string.IsNullOrWhiteSpace(mm.Groups["const"].Value);
            
            if (result)
            {
                token = mm.Groups["const"].Value;
                text = text.Substring(token.Length);
            }

            return result;
        }

        private const string rxText = @"^(\""(?<const>[^\""]*)\"")";
        private bool TokenIsTextConstant(ref string text, ref string token)
        {
            var mm = Regex.Match(text, rxText);
            var result = mm.Success && mm.Groups["const"].Success;
            if (result)
            {
                token = mm.Groups["const"].Value;
                text = text.Substring(mm.Groups[0].Value.Length);
            }

            return result;
        }

        private const string rxGlobal = @"^((User!)|(Globals!))(?<const>[a-z, A-Z]*)";
        private bool TokenIsGlobalField(ref string text, ref string token)
        {
            var mm = Regex.Match(text, rxGlobal);
            var result = mm.Success && !string.IsNullOrWhiteSpace(mm.Groups["const"].Value);
            if (result)
            {
                token = mm.Groups["const"].Value;
                text = text.Substring(mm.Groups[0].Value.Length);
            }

            return result;
        }

        private const string rxParameter = @"^Parameters!(?<name>[a-z,A-Z]([a-z,A-Z,0-9,_])*)\.Value";
        private bool TokenIsParameter(ref string text, ref string token)
        {
            var mm = Regex.Match(text, rxParameter);
            var result = mm.Success && !string.IsNullOrWhiteSpace(mm.Groups["name"].Value);
            if (result)
            {
                token = mm.Groups["name"].Value;
                text = text.Substring(mm.Groups[0].Value.Length);
            }

            return result;
        }

        private const string rxFunction = @"^(?<name>[a-z,A-Z]([a-z,A-Z,0-9])*)[\s]*\(";
        private bool TokenIsFunction(ref string text, ref string token)
        {
            var mm = Regex.Match(text, rxFunction);
            var result = mm.Success && !string.IsNullOrWhiteSpace(mm.Groups["name"].Value);
            if (result)
            {
                token = mm.Groups["name"].Value;
                text = text.Substring(mm.Groups[0].Value.Length);
            }

            return result;
        }

        private bool TokenIsOperator(ref string text, ref string token)
        {
            var keyword = OperatorKeywords.Select(k => k.Key).FirstOrDefault(text.StartsWith);
            var isOperator = !string.IsNullOrWhiteSpace(keyword);
            if (isOperator)
            {
                token = keyword;
                text = text.Substring(token.Length);
            }

            return isOperator;
        }

        private bool TokenIsEnding(ref string text, ref EndingReason reason)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                reason = EndingReason.EndOfText;
                return true;
            }

            var keyword = EndingKeywords.Select(k => k.Key).FirstOrDefault(text.StartsWith);
            var isEnding = !string.IsNullOrWhiteSpace(keyword);
            if (isEnding)
            {
                reason = EndingKeywords[keyword];
                text = text.Substring(keyword.Length);
            }
            return isEnding;
        }

        private Expression GetGlobalFieldExpression(string fieldName)
        {
            var field = (GlobalField)Enum.Parse(typeof(GlobalField), fieldName);
            object value = null;
            value = GetGlobalValue != null ? GetGlobalValue(field) : GetGlobalDefaultValue(field);
            return Expression.Constant(value);
        }

        private object GetGlobalDefaultValue(GlobalField field)
        {
            switch (field)
            {
                case GlobalField.ExecutionTime:
                    return DateTime.Now;

                case GlobalField.Language:
                    return Thread.CurrentThread.CurrentUICulture.Name;

                case GlobalField.ReportFolder:
                case GlobalField.ReportName:
                case GlobalField.UserID:
                    return null;
            }

            return null;
        }

        private Expression GetParameterExpression(string paramName)
        {
            if (GetParameterValue != null)
            {
                return Expression.Constant(GetParameterValue(paramName));
            }

            throw new RdlExpressionSyntaxException("Parameter {0} is undefined", paramName);
        }

        private Expression GetOperatorExpression(Expression expr, string keyword, Expression secondOperand)
        {
            var resExpr = OperatorKeywords[keyword](expr, secondOperand);

            if (!(secondOperand is BinaryExpression))
            {
                return resExpr;
            }
            
            var secExpr = (BinaryExpression)secondOperand;
            
            var precOp1 = OperatorPrecedence[resExpr.NodeType];
            var precOp2 = OperatorPrecedence[secExpr.NodeType];

            if (precOp1 < precOp2)
            {
                resExpr = OperatorKeywords[keyword](expr, secExpr.Left);
                return secExpr.Update(resExpr, secExpr.Conversion, secExpr.Right);
            }

            return resExpr;
        }

        private Expression GetFunctionExpression(ref string exprText, string function)
        {
            var endingReason = EndingReason.ListSeparator;
            List<Expression> args = new List<Expression>();

            while (endingReason != EndingReason.ClosingBracket)
            {
                Expression paramExpr = Expression.Empty();
                endingReason = Parse(ref paramExpr, ref exprText);
                switch (endingReason)
                {
                    case EndingReason.ClosingBracket:
                        if (paramExpr.NodeType != ExpressionType.Default)
                        {
                            args.Add(paramExpr);
                        }
                        return Expression.Call(typeof(Functions), function, null, args.ToArray());

                    case EndingReason.ListSeparator:
                        args.Add(paramExpr);
                        break;

                    default:
                        throw new RdlExpressionSyntaxException("Missing closing bracket for function " + function);
                }
            }

            return Expression.Empty();
        }
    }

    public class RdlExpressionSyntaxException : Exception
    {
        private const string Message = "RDL expression syntax error.";

        public RdlExpressionSyntaxException(string description = "", params object[] args) : 
            base(Message + " " + string.Format(description, args))
        {
        }
    }
}
