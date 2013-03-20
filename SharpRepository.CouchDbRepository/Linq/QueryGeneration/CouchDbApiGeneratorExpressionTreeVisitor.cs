﻿using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;
using Remotion.Linq.Clauses.ExpressionTreeVisitors;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Parsing;

namespace SharpRepository.CouchDbRepository.Linq.QueryGeneration
{
    public class CouchDbApiGeneratorExpressionTreeVisitor : ThrowingExpressionTreeVisitor
    {
        public static string GetCouchDbApiExpression (Expression linqExpression)
        {
          var visitor = new CouchDbApiGeneratorExpressionTreeVisitor ();
          visitor.VisitExpression (linqExpression);
          return visitor.GetCouchDbApiExpression();
        }

        private readonly StringBuilder _expression = new StringBuilder ();

        private CouchDbApiGeneratorExpressionTreeVisitor()
        {
        }

        public string GetCouchDbApiExpression()
        {
          return _expression.ToString ();
        }

        protected override Expression VisitQuerySourceReferenceExpression (QuerySourceReferenceExpression expression)
        {
            _expression.Append("doc"); // not sure if this will always be static like this, need to check for joins
            //_expression.Append (expression.ReferencedQuerySource.ItemName);
            return expression;
        }

        protected override Expression VisitSubQueryExpression(SubQueryExpression expression)
        {
            return base.VisitSubQueryExpression(expression);
        }

        protected override Expression VisitBinaryExpression (BinaryExpression expression)
        {
            _expression.Append ("(");

            // since the date field is just stored as text via json within CouchDb, to do equality checks against a date field you must do it by doing:
            //  new Date(doc.DateField) == new Date('1/1/2012')
            var isLeftDateMember = (expression.Left.NodeType == ExpressionType.MemberAccess && (expression.Left.Type == typeof(DateTime) || expression.Left.Type == typeof(DateTime?)));
            if (isLeftDateMember)
            {
                _expression.Append("new Date(");
            }

            VisitExpression (expression.Left);

            if (isLeftDateMember)
            {
                _expression.Append(")");
            }

            // In production code, handle this via lookup tables.
            switch (expression.NodeType)
            {
                case ExpressionType.Equal:
                    _expression.Append (" == ");
                    break;

                case ExpressionType.NotEqual:
                    _expression.Append (" != ");
                    break;

                case ExpressionType.GreaterThan:
                    _expression.Append (" > ");
                    break;

                case ExpressionType.GreaterThanOrEqual:
                    _expression.Append (" >= ");
                    break;

                case ExpressionType.LessThan:
                    _expression.Append (" < ");
                    break;

                case ExpressionType.LessThanOrEqual:
                    _expression.Append (" <= ");
                    break;

                case ExpressionType.AndAlso:
                case ExpressionType.And:
                    _expression.Append (" && ");
                    break;
        
                case ExpressionType.OrElse:
                case ExpressionType.Or:
                    _expression.Append (" || ");
                    break;

                case ExpressionType.Add:
                    _expression.Append (" + ");
                    break;

                case ExpressionType.Subtract:
                    _expression.Append (" - ");
                    break;

                case ExpressionType.Multiply:
                    _expression.Append (" * ");
                    break;

                case ExpressionType.Divide:
                    _expression.Append (" / ");
                    break;

                default:
                    base.VisitBinaryExpression (expression);
                    break;
            }

            var isRightDateMember = (expression.Right.NodeType == ExpressionType.MemberAccess && (expression.Right.Type == typeof(DateTime) || expression.Right.Type == typeof(DateTime?)));
            if (isRightDateMember)
            {
                _expression.Append("new Date(");
            }

            VisitExpression (expression.Right);

            if (isRightDateMember)
            {
                _expression.Append(")");
            }

            _expression.Append (")");

            return expression;
        }

        protected override Expression VisitMemberExpression (MemberExpression expression)
        {
            VisitExpression (expression.Expression);
            _expression.AppendFormat(".{0}", expression.Member.Name);

          return expression;
        }

        protected override Expression VisitConstantExpression (ConstantExpression expression)
        {           
            // check to see if we don't need the quotes
            var quotes = "'";
            var value = expression.Value.ToString();
            if (
                expression.Type == typeof(Int32)
                || expression.Type == typeof(Int16)
                || expression.Type == typeof(Int64)
                || expression.Type == typeof(Decimal)
                || expression.Type == typeof(Double)
                || expression.Type == typeof(Boolean)
                )
            {
                quotes = "";
            }
            else if (expression.Type == typeof(DateTime) || expression.Type == typeof(DateTime?))
            {
                quotes = "";
                value = String.Format("new Date('{0}')", value);
            }

             _expression.AppendFormat("{1}{0}{1}", value, quotes);

          return expression;
        }

        protected override Expression VisitNewExpression(NewExpression expression)
        {
            // for new { c.Name, c.Title, c.TItle.Length }

            // expression.Members has all the property names of the anonymous type
            //  e.g. String Name, String Title, Int32 Length

            // expression.Arguments has the expressions for getting the value, so these need to be run through the Visit stuff to get their 
            //  e.g. [10001].Name, [10001].Title, [10001].Title.Length

            _expression.Append("{");

            var i = 0;
            foreach (var arg in expression.Arguments)
            {
                if (i != 0)
                    _expression.Append(",");

                _expression.AppendFormat("{0}: ", expression.Members[i].Name);
                VisitExpression(arg);
                i++;
            }
            _expression.Append("}");


            return expression;

            return base.VisitNewExpression(expression);
        }

        protected override Expression VisitMethodCallExpression (MethodCallExpression expression)
        {
          // In production code, handle this via method lookup tables.

            if (expression.Method.Name == "Contains")
            {
                VisitExpression (expression.Object);
                _expression.Append (".indexOf(");
                VisitExpression (expression.Arguments[0]);
                _expression.Append (") != -1");
                return expression;
            }

            if (expression.Method.Name == "StartsWith")
            {
                VisitExpression(expression.Object);
                _expression.Append(".indexOf(");
                VisitExpression(expression.Arguments[0]);
                _expression.Append(") == 0");
                return expression;
            }

            if (expression.Method.Name == "EndsWith")
            {
                VisitExpression(expression.Object);
                _expression.Append(".indexOf(");
                VisitExpression(expression.Arguments[0]);
                _expression.Append(", ");
                VisitExpression(expression.Object);
                _expression.Append(".length - '");
                VisitExpression(expression.Arguments[0]);
                _expression.Append("'.length) != -1");
                return expression;
            }

            return base.VisitMethodCallExpression (expression); // throws
        }

        // Called when a LINQ expression type is not handled above.
        protected override Exception CreateUnhandledItemException<T>(T unhandledItem, string visitMethod)
        {
          string itemText = FormatUnhandledItem(unhandledItem);
          var message = string.Format ("The expression '{0}' (type: {1}) is not supported by this LINQ provider.", itemText, typeof (T));
          return new NotSupportedException (message);
        }

        private static string FormatUnhandledItem<T>(T unhandledItem)
        {
          var itemAsExpression = unhandledItem as Expression;
          return itemAsExpression != null ? FormattingExpressionTreeVisitor.Format (itemAsExpression) : unhandledItem.ToString ();
        }
    }
}
