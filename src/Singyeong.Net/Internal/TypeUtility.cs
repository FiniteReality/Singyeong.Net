using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace Singyeong.Internal
{
    internal static class TypeUtility
    {
        public static bool IsSupported<T>()
        {
            return IsSupported(typeof(T));
        }

        public static bool IsSupported(Type type)
        {
            // TODO: support singyeong "version" type

            return type == typeof(char) || type == typeof(string) ||
                type == typeof(byte) || type == typeof(sbyte) ||
                type == typeof(ushort) || type == typeof(short) ||
                type == typeof(uint) || type == typeof(int) ||
                type == typeof(ulong) || type == typeof(long) ||
                type == typeof(float) || type == typeof(double) ||
                IsSupportedList(type);

            static bool IsSupportedList(Type listType)
            {
                if (!listType.IsGenericType)
                    return false;

                if (listType.GetGenericTypeDefinition() != typeof(List<>))
                    return false;

                return IsSupported(listType.GenericTypeArguments[0]);
            }
        }

        public static string GetTypeName<T>(T value)
        {
            Debug.Assert(IsSupported<T>());

            switch (value)
            {
                case string _:
                case char _:
                    return "string";

                case byte _:
                case sbyte _:
                case ushort _:
                case short _:
                case uint _:
                case int _:
                case ulong _:
                case long _:
                    return "integer";

                case float _:
                case double _:
                    return "float";

                case IList _:
                    return "list";

                // Should never happen but necessary to prevent CS0161
                default:
                    throw new ArgumentException("Invalid type passed",
                        nameof(value));
            }
        }
    }
}
