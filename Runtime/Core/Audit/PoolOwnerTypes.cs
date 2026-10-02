using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace RetrofitLocalization
{
    /// <summary>
    /// Finds the types that own string pools and makes them register.
    ///
    /// A pool registers itself when its owning type is first used, because that is when the static
    /// field initializer that calls <c>LocalizePool</c> runs. An audit that only looked at what
    /// happens to be registered would silently skip every type nothing has touched yet. So the
    /// audit first runs the static constructor of every candidate type.
    ///
    /// Used by tooling only; nothing here runs in a normal play session.
    /// </summary>
    public static class PoolOwnerTypes
    {
        /// <summary>
        /// True when <paramref name="type"/> declares a static <c>string[]</c> or
        /// <c>List&lt;string&gt;</c> field, public or not.
        /// </summary>
        public static bool DeclaresStringPools(Type type)
        {
            // An open generic type has no static constructor that could be run.
            if (type == null || type.ContainsGenericParameters) return false;

            FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic
                                                | BindingFlags.Static | BindingFlags.DeclaredOnly);
            foreach (FieldInfo field in fields)
            {
                if (field.FieldType == typeof(string[]) || field.FieldType == typeof(List<string>)) return true;
            }

            return false;
        }

        /// <summary>
        /// Runs the static constructor of every type in <paramref name="assemblies"/> that declares
        /// string pools. Nested types count as part of the namespace of their outermost type.
        /// </summary>
        /// <param name="assemblies">Assemblies to scan.</param>
        /// <param name="namespaces">
        /// Namespaces to include, each matching itself and everything below it. Null or empty
        /// includes every namespace.
        /// </param>
        /// <param name="failures">
        /// Receives one line per type whose static constructor threw. Such a type needs run-time
        /// state the tool does not have (a loaded save, a scene); its pools are not covered.
        /// </param>
        /// <returns>The number of types that were initialised.</returns>
        public static int Initialize(IEnumerable<Assembly> assemblies, IReadOnlyCollection<string> namespaces,
            ICollection<string> failures)
        {
            if (assemblies == null) throw new ArgumentNullException(nameof(assemblies));

            int initialised = 0;
            foreach (Assembly assembly in assemblies)
            {
                if (assembly == null) continue;

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types; // the types that did load; the rest are null
                }

                foreach (Type type in types)
                {
                    if (type == null || !IsInNamespaces(type, namespaces) || !DeclaresStringPools(type)) continue;

                    try
                    {
                        RuntimeHelpers.RunClassConstructor(type.TypeHandle);
                        initialised++;
                    }
                    catch (TypeInitializationException e)
                    {
                        Exception cause = e.InnerException ?? e;
                        failures?.Add(type.FullName + ": " + cause.GetType().Name + " - " + cause.Message);
                    }
                }
            }

            return initialised;
        }

        private static bool IsInNamespaces(Type type, IReadOnlyCollection<string> namespaces)
        {
            if (namespaces == null || namespaces.Count == 0) return true;

            Type outermost = type;
            while (outermost.DeclaringType != null) outermost = outermost.DeclaringType;
            string typeNamespace = outermost.Namespace ?? string.Empty;

            foreach (string candidate in namespaces)
            {
                if (string.IsNullOrEmpty(candidate)) continue;
                if (typeNamespace.Equals(candidate, StringComparison.Ordinal)
                    || typeNamespace.StartsWith(candidate + ".", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
