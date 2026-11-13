// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license. 
// See license.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

#if NET_20
namespace System.Runtime.CompilerServices
{
    // Summary:
    //     Indicates that a method is an extension method, or that a class or assembly
    //     contains extension methods.
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
    internal sealed class ExtensionAttribute : Attribute
    {
        // Summary:
        //     Initializes a new instance of the System.Runtime.CompilerServices.ExtensionAttribute
        //     class.
        public ExtensionAttribute() { }
    }
}
#endif

namespace NUglify
{
	static class ReflectionHelper
    {
#if NETPRE45
        public static Type GetTypeInfo(this Type type)
        {
            return type;
        }
        public static IEnumerable<FieldInfo> GetDeclaredFields(this Type type)
        {
            return
                type.GetFields(BindingFlags.Public | BindingFlags.DeclaredOnly | BindingFlags.Instance |
                               BindingFlags.Static);
        }
        public static IEnumerable<PropertyInfo> GetDeclaredProperties(this Type type)
        {
            return
                type.GetProperties(BindingFlags.Public | BindingFlags.DeclaredOnly | BindingFlags.Instance |
                                   BindingFlags.Static);
        }
        public static object GetValue(this PropertyInfo propInfo, object obj)
        {
            return propInfo.GetValue(obj, null);
        }
        public static void SetValue(this PropertyInfo propInfo, object obj, object value)
        {
            propInfo.SetValue(obj, value, null);
        }
        public static IEnumerable<MethodInfo> GetDeclaredMethods(this Type type)
        {
            return type.GetMethods(BindingFlags.Public|BindingFlags.Static|BindingFlags.DeclaredOnly|BindingFlags.Instance);
        }
        public static T GetCustomAttribute<T>(this MemberInfo memberInfo) where T : Attribute
        {
            foreach (var attribute in memberInfo.GetCustomAttributes(true))
            {
                var attributeT = attribute as T;
                if (attributeT != null)
                {
                    return attributeT;
                }
            }
            return (T)null;
        }

        public static void Clear(this StringBuilder @this)
        {
           @this.Length = 0;
        }
#endif
    }
}
