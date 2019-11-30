using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;

namespace Singyeong.Internal
{
    internal static class QueryCompiler
    {
        // TODO: this query compiler is very basic and only supports the
        // absolute minimum. It is likely **very** incorrect and will fall over
        // at the first chance it gets. NotImplementedexception will be thrown

        private static readonly MethodInfo MetadataMethod =
            typeof(SingyeongQuery).GetMethod("Metadata",
                BindingFlags.Public | BindingFlags.Instance)!;
        private static readonly MethodInfo ContainsExtensionMethod =
            typeof(Enumerable).GetMethods()
                .Where(x => x.Name == "Contains")
                .Single(x => x.GetParameters().Length == 2);
        private static readonly Type IConvertibleType = typeof(IConvertible);

        public static void WriteQuery(Utf8JsonWriter writer,
            Expression<Func<SingyeongQuery, bool>> query)
        {
            writer.WriteStartArray();

            WriteSubQuery(query.Body, writer);

            writer.WriteEndArray();
        }

        private static void WriteSubQuery(Expression expression,
            Utf8JsonWriter writer)
        {
            writer.WriteStartObject();

            if (expression is BinaryExpression binaryExpression)
            {
                switch (binaryExpression.NodeType)
                {
                    case ExpressionType.AndAlso:
                        writer.WriteStartArray("$and");
                        WriteSubQuery(binaryExpression.Left, writer);
                        WriteSubQuery(binaryExpression.Right, writer);
                        writer.WriteEndArray();
                        break;
    
                    case ExpressionType.OrElse:
                        writer.WriteStartArray("$or");
                        WriteSubQuery(binaryExpression.Left, writer);
                        WriteSubQuery(binaryExpression.Right, writer);
                        writer.WriteEndArray();
                        break;

                    case ExpressionType.Equal:
                    {
                        if (!TryFindKeyValue(binaryExpression, out var key,
                            out var value))
                            throw new ArgumentException(
                                "Could not find key and/or value",
                                nameof(expression));

                        writer.WriteStartObject(key);
                        writer.WritePropertyName("$eq");
                        WriteValue(value, writer);
                        writer.WriteEndObject();
                        break;
                    }

                    case ExpressionType.NotEqual:
                    {
                        if (!TryFindKeyValue(binaryExpression, out var key,
                            out var value))
                            throw new ArgumentException(
                                "Could not find key and/or value",
                                nameof(expression));

                        writer.WriteStartObject(key);
                        writer.WritePropertyName("$ne");
                        WriteValue(value, writer);
                        writer.WriteEndObject();
                        break;
                    }

                    case ExpressionType.GreaterThan:
                    {
                        if (!TryFindKeyValue(binaryExpression, out var key,
                            out var value))
                            throw new ArgumentException(
                                "Could not find key and/or value",
                                nameof(expression));

                        writer.WriteStartObject(key);
                        writer.WritePropertyName("$gt");
                        WriteValue(value, writer);
                        writer.WriteEndObject();
                        break;
                    }

                    case ExpressionType.GreaterThanOrEqual:
                    {
                        if (!TryFindKeyValue(binaryExpression, out var key,
                            out var value))
                            throw new ArgumentException(
                                "Could not find key and/or value",
                                nameof(expression));

                        writer.WriteStartObject(key);
                        writer.WritePropertyName("$gte");
                        WriteValue(value, writer);
                        writer.WriteEndObject();
                        break;
                    }

                    case ExpressionType.LessThan:
                    {
                        if (!TryFindKeyValue(binaryExpression, out var key,
                            out var value))
                            throw new ArgumentException(
                                "Could not find key and/or value",
                                nameof(expression));

                        writer.WriteStartObject(key);
                        writer.WritePropertyName("$lt");
                        WriteValue(value, writer);
                        writer.WriteEndObject();
                        break;
                    }

                    case ExpressionType.LessThanOrEqual:
                    {
                        if (!TryFindKeyValue(binaryExpression, out var key,
                            out var value))
                            throw new ArgumentException(
                                "Could not find key and/or value",
                                nameof(expression));

                        writer.WriteStartObject(key);
                        writer.WritePropertyName("$lte");
                        WriteValue(value, writer);
                        writer.WriteEndObject();
                        break;
                    }

                    default:
                    {
                        throw new NotImplementedException(
                            $"Expression {binaryExpression} is not supported");
                    }
                }
            }
            else if (expression is UnaryExpression unary
                && unary.NodeType == ExpressionType.Not)
            {
                if (unary.Operand is MethodCallExpression notContains
                    && TryGetContains(notContains, out var key, out var value,
                    out var isMetadataList))
                {
                    writer.WriteStartObject(key);
                    writer.WritePropertyName(isMetadataList
                        ? "$ncontains" : "$nin");
                    WriteValue(value, writer);
                    writer.WriteEndObject();
                }
            }
            else if (expression is MethodCallExpression methodCall
                && TryGetContains(methodCall, out var key, out var value,
                    out var isMetadataList))
            {
                writer.WriteStartObject(key);
                writer.WritePropertyName(isMetadataList ? "$contains" : "$in");
                WriteValue(value, writer);
                writer.WriteEndObject();
            }
            else
            {
                throw new NotImplementedException(
                    $"Expression {expression} is not supported");
            }

            writer.WriteEndObject();
        }

        private static void WriteValue(Expression expression,
            Utf8JsonWriter writer)
        {
            if (expression.Type == typeof(string))
            {
                if (TryGetExpressionValue(expression, out string? value))
                    writer.WriteStringValue(value);
            }
            else if (IConvertibleType.IsAssignableFrom(expression.Type))
            {
                if (TryGetExpressionValue(expression, out IConvertible? value))
                {
                    switch (Type.GetTypeCode(expression.Type))
                    {
                        case TypeCode.Byte:
                            writer.WriteNumberValue((byte)value);
                            break;
                        case TypeCode.UInt16:
                            writer.WriteNumberValue((ushort)value);
                            break;
                        case TypeCode.UInt32:
                            writer.WriteNumberValue((uint)value);
                            break;
                        case TypeCode.UInt64:
                            writer.WriteNumberValue((ulong)value);
                            break;
                        case TypeCode.SByte:
                            writer.WriteNumberValue((sbyte)value);
                            break;
                        case TypeCode.Int16:
                            writer.WriteNumberValue((short)value);
                            break;
                        case TypeCode.Int32:
                            writer.WriteNumberValue((int)value);
                            break;
                        case TypeCode.Int64:
                            writer.WriteNumberValue((long)value);
                            break;
                        case TypeCode.Single:
                            writer.WriteNumberValue((float)value);
                            break;
                        case TypeCode.Double:
                            writer.WriteNumberValue((double)value);
                            break;
                        case TypeCode.Decimal:
                            writer.WriteNumberValue((decimal)value);
                            break;
                        case TypeCode.Boolean:
                            writer.WriteBooleanValue((bool)value);
                            break;
                        case TypeCode.DateTime:
                            writer.WriteStringValue((DateTime)value);
                            break;

                        default:
                            throw new NotImplementedException(
                                $"Expression {expression} is not supported");
                    }
                }
            }
        }

        private static bool TryFindKeyValue(BinaryExpression expression,
            [NotNullWhen(true)]out string? key,
            [NotNullWhen(true)]out Expression? value)
        {

            if (expression.Left is MethodCallExpression methodCallLeft
                && TryGetMetadataName(methodCallLeft, out key))
            {
                value = expression.Right;
                return true;
            }
            else if (expression.Right is MethodCallExpression methodCallRight
                && TryGetMetadataName(methodCallRight, out key))
            {
                value = expression.Right;
                return true;
            }

            key = default;
            value = default;
            return false;
        }

        private static bool TryGetContains(MethodCallExpression methodCall,
            [NotNullWhen(true)] out string? name,
            [NotNullWhen(true)] out Expression? expression,
            out bool isMetadataList)
        {
            name = default;
            expression = default;
            isMetadataList = default;

            if (methodCall.Method != ContainsExtensionMethod)
                return false;

            for (int i = 0; i < methodCall.Arguments.Count; i++)
            {
                var argument = methodCall.Arguments[i];

                if (argument is MethodCallExpression metadataCall
                    && TryGetMetadataName(metadataCall, out name))
                {
                    isMetadataList = i == 0;
                }
                else
                {
                    expression = argument;
                }
            }

            if (!(name is {}))
                return false;

            return true;
        }

        private static bool TryGetMetadataName(MethodCallExpression methodCall,
            [NotNullWhen(true)] out string? name)
        {
            name = default;

            if (!methodCall.Method.IsGenericMethod ||
                methodCall.Method.GetGenericMethodDefinition()
                    != MetadataMethod)
                return false;

            var expression = methodCall.Arguments[0];

            return TryGetExpressionValue(expression, out name);
        }

        private static bool TryGetExpressionValue<T>(Expression expression,
            [NotNullWhen(true)] out T? value)
            where T : class
        {
            value = default;

            if (expression is ConstantExpression constant)
            {
                if (!(constant.Value is T constantValue))
                    return false;

                value = constantValue;
                return true;
            }
            else if (expression is MemberExpression member)
            {
                if (member.Type != typeof(T))
                    return false;

                value = Expression.Lambda<Func<T>>(expression)
                    .Compile()();
                return true;
            }

            return false;
        }
    }
}