﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Rocket.API;

namespace Rocket.Core.Extensions
{
    public static class ReflectionExtensions
    {
        public static IEnumerable<Type> FindTypes<T>(this ILifecycleObject @object,
                                                     bool includeAbstractAndInterfaces = true,
                                                     Func<Type, bool> predicate = null)
        {
            IEnumerable<Type> filter = @object.FindAllTypes(includeAbstractAndInterfaces)
                                              .Where(t => typeof(T).IsAssignableFrom(t));

            if (predicate != null) filter = filter.Where(predicate);

            return filter;
        }

        public static MethodBase GetCallingMethod(Type[] skipTypes = null, MethodBase[] skipMethods = null, bool applyAsyncMethodPatch = true)
        {
            var skipList = new List<Type>(skipTypes ?? new Type[0]);
            skipList.Add(typeof(ReflectionExtensions));

            StackTrace st = new StackTrace();
            StackFrame target = null;
            for (int i = 0; i < st.FrameCount; i++)
            {
                StackFrame frame = st.GetFrame(i);
                var frameMethod = frame.GetMethod();
                if(frameMethod == null)
                    continue;

                // Hot fix for async Task methods:
                // If current frame method is called "MoveNext" and parent frame is from "AsyncMethodBuilderCore" type 
                //   it's an async method wrapper, so we need to skip these two frames to get the original calling async method
                // Tested on .NET Core 2.1; should be tested on full .NET and mono too
                if (applyAsyncMethodPatch && frameMethod is MethodInfo currentMethodFrameInfo && currentMethodFrameInfo.Name == "MoveNext")
                {
                    var tmpIndex = i;
                    var frameOriginal = frame;

                    frame = st.GetFrame(++tmpIndex);
                    frameMethod = frame.GetMethod();

                    // Check parent frame - if its from AsyncMethodBuilderCore, its definitely an async Task
                    if (frameMethod is MethodInfo parentFrameMethodInfo && 
                        (parentFrameMethodInfo.DeclaringType?.Name == "AsyncMethodBuilderCore"
                            || parentFrameMethodInfo.DeclaringType?.Name == "AsyncTaskMethodBuilder"))
                    {
                        frame = st.GetFrame(++tmpIndex);
                        frameMethod = frame.GetMethod();

                        i = tmpIndex;
                    }
                    else
                    {
                        //Restore original frame
                        frame = frameOriginal;
                        frameMethod = frameOriginal.GetMethod();
                    }
                }

                if (skipList.Any(c => c == frameMethod?.DeclaringType))
                    continue;

                if (skipMethods?.Any(c => c == frameMethod) ?? false)
                    continue;
                target = frame;
                break;
            }

            return target?.GetMethod();
        }

        public static MethodBase GetCallingMethod(params Assembly[] skipAssemblies)
        {
            StackTrace st = new StackTrace();
            StackFrame target = null;
            for (int i = 0; i < st.FrameCount; i++)
            {
                StackFrame frame = st.GetFrame(i);
                if (skipAssemblies.Any(c => Equals(c, frame.GetMethod()?.DeclaringType?.Assembly)))
                    continue;

                target = frame;
            }

            return target?.GetMethod();
        }

        public static IEnumerable<Type> GetTypeHierarchy(this Type type)
        {
            List<Type> types = new List<Type> { type};
            while ((type = type.BaseType) != null)
            {
                types.Add(type);
            }

            return types;
        }

        internal static T GetPrivateProperty<T>(this object o, string property)
        {
            var prop = o.GetType()
                        .GetProperty(property, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

            if(prop == null)
                throw new Exception("Property not found!");

            return (T) prop.GetGetMethod(true)
                    .Invoke(o, new object[0]);
        }

        public static IEnumerable<Type> FindAllTypes(this ILifecycleObject @object,
                                                     bool includeAbstractAndInterfaces = false)
            => @object.GetType().Assembly.FindAllTypes(includeAbstractAndInterfaces);

        public static IEnumerable<Type> FindAllTypes(this Assembly @object, bool includeAbstractAndInterfaces = false)
        {
            try
            {
                return @object.GetTypes()
                              .Where(c => includeAbstractAndInterfaces || !c.IsAbstract && !c.IsInterface);
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.Where(t => t != null);
            }
        }

        public static IEnumerable<Type> FindTypes<T>(this Assembly @object, bool includeAbstractAndInterfaces = false)
        {
            return FindAllTypes(@object).Where(c => typeof(T).IsAssignableFrom(c));
        }

        public static IEnumerable<Type> GetTypesWithInterface<TInterface>(this Assembly assembly)
        {
            return assembly.FindAllTypes().Where(t => typeof(TInterface).IsAssignableFrom(t));
        }

        public static Dictionary<string, string> GetAssembliesFromDirectory(
            string directory, string extension = "*.dll")
        {
            Dictionary<string, string> l = new Dictionary<string, string>();
            IEnumerable<FileInfo> assemblyFiles =
                new DirectoryInfo(directory).GetFiles(extension, SearchOption.AllDirectories);
            foreach (FileInfo assemblyFile in assemblyFiles)
                try
                {
                    AssemblyName assemblyName = AssemblyName.GetAssemblyName(assemblyFile.FullName);
                    l.Add(GetVersionIndependentName(assemblyName.FullName), assemblyFile.FullName);
                }
                catch
                {

                }

            return l;
        }

        public static string GetVersionIndependentName(string name)
        {
            return GetVersionIndependentName(name, out _);
        }

        private static readonly Regex versionRegex = new Regex("Version=(?<version>.+?), ", RegexOptions.Compiled);
        public static string GetVersionIndependentName(string name, out string extractedVersion)
        {
            var match = versionRegex.Match(name);
            extractedVersion = match.Groups[1].Value;
            return versionRegex.Replace(name, "");
        }

        public static string GetDebugName(this MethodBase mb)
        {
            if (mb is MemberInfo mi && mi.DeclaringType != null) return mi.DeclaringType.Name + "." + mi.Name;

            return "<anonymous>#" + mb.Name;
        }

        public static async Task InvokeWithTaskSupport(this MethodBase method, object instance, object[] @params)
        {
            bool isAsync = false;
            if (method is MethodInfo methodInfo)
            {
                var returntype = methodInfo.ReturnType;
                isAsync = typeof(Task).IsAssignableFrom(returntype);
            }

            if (isAsync)
            {
                var task = (Task)method.Invoke(instance, @params);
                await task;
                return;
            }

            method.Invoke(instance, @params.ToArray());
        }
    }
}