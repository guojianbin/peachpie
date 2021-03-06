﻿using Pchp.Core;
using Pchp.Core.Reflection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Pchp.Library.Resources;
using Pchp.Library.Reflection;

namespace Pchp.Library
{
    public static class Objects
    {
        /// <summary>
		/// Tests whether the class given by <paramref name="classNameOrObject"/> is derived from a class given by <paramref name="baseClassName"/>.
		/// </summary>
        /// <param name="ctx">Runtime context.</param>
        /// <param name="classNameOrObject">The object or the name of a class (<see cref="string"/>).</param>
		/// <param name="baseClassName">The name of the (base) class.</param>
		/// <returns><B>true</B> if <paramref name="classNameOrObject"/> implements or extends <paramref name="baseClassName"/>, <B>false</B> otherwise.</returns>
		public static bool is_subclass_of(Context ctx, PhpValue classNameOrObject, string baseClassName)
        {
            var tinfo = TypeNameOrObjectToType(ctx, classNameOrObject);
            if (tinfo == null) return false;

            // look for the class, do not use autoload (since PHP 5.1):
            var base_tinfo = ctx.GetDeclaredType(baseClassName, false);
            if (base_tinfo == null) return false;

            //
            return base_tinfo.Type.IsAssignableFrom(tinfo.Type);
        }

        /// <summary>
		/// Tests whether a given class is defined.
		/// </summary>
        /// <param name="ctx">Current runtime context.</param>
        /// <param name="className">The name of the class.</param>
		/// <param name="autoload">Whether to attempt to call <c>__autoload</c>.</param>
		/// <returns><B>true</B> if the class given by <paramref name="className"/> has been defined,
		/// <B>false</B> otherwise.</returns>
		public static bool class_exists(Context ctx, string className, bool autoload = true)
            => ctx.GetDeclaredType(className, autoload) != null;

        /// <summary>
		/// Tests whether a given interface is defined.
		/// </summary>
        /// <param name="ctx">Current runtime context.</param>
        /// <param name="ifaceName">The name of the interface.</param>
		/// <param name="autoload">Whether to attempt to call <c>__autoload</c>.</param>
		/// <returns><B>true</B> if the interface given by <paramref name="ifaceName"/> has been defined,
		/// <B>false</B> otherwise.</returns>
		public static bool interface_exists(Context ctx, string ifaceName, bool autoload = true)
        {
            var info = ctx.GetDeclaredType(ifaceName, autoload);
            return info != null && info.IsInterface;
        }

        /// <summary>
		/// Tests whether a given trait is defined.
		/// </summary>
        /// <param name="ctx">Current runtime context.</param>
        /// <param name="traitname">The name of the trait.</param>
		/// <param name="autoload">Whether to attempt to call <c>__autoload</c>.</param>
		/// <returns><B>true</B> if the trait given by <paramref name="traitname"/> has been defined,
		/// <B>false</B> otherwise.</returns>
		public static bool trait_exists(Context ctx, string traitname, bool autoload = true)
        {
            var info = ctx.GetDeclaredType(traitname, autoload);
            return info != null && info.IsTrait;
        }

        /// <summary>
        /// Returns the name of the callers class context.
        /// </summary>
        /// <param name="tctx">Current class context.</param>
        /// <returns>Current class name.</returns>
        [return: CastToFalse]
        public static string get_class([ImportCallerClass]string tctx)
        {
            return tctx;
        }

        /// <summary>
        /// Returns the name of the class of which the object <paramref name="obj"/> is an instance.
        /// </summary>
        /// <param name="tctx">Current class context.</param>
        /// <param name="obj">The object whose class is requested.</param>
        /// <returns><paramref name="obj"/>'s class name or current class name if <paramref name="obj"/> is <B>null</B>.</returns>
        [return: CastToFalse]
        public static string get_class([ImportCallerClass]string tctx, PhpValue obj)
        {
            if (obj.IsSet)
            {
                if (obj.IsObject)
                {
                    return obj.Object.GetPhpTypeInfo().Name;
                }
                else if (obj.IsAlias)
                {
                    return get_class(tctx, obj.Alias.Value);
                }
                else
                {
                    // TODO: E_WARNING
                    throw new ArgumentException(nameof(obj));
                }
            }

            return tctx;
        }

        /// <summary>
        /// Gets the name of the class the static method is called in.
        /// </summary>
        [return: CastToFalse]
        public static string get_called_class([ImportCallerStaticClass]PhpTypeInfo @static) => @static?.Name;

        /// <summary>
        /// Helper getting declared classes or interfaces.
        /// </summary>
        /// <param name="ctx">Runtime context with declared types.</param>
        /// <param name="interfaces">Whether to list interfaces or classes.</param>
        /// <param name="traits">Whether to list traits.</param>
        static PhpArray get_declared_types(Context ctx, bool interfaces, bool traits)
        {
            var result = new PhpArray();

            foreach (var t in ctx.GetDeclaredTypes())
            {
                if (t.IsInterface == interfaces && t.IsTrait == traits)
                {
                    result.Add(t.Name);
                }
            }

            return result;
        }

        /// <summary>
		/// Returns a <see cref="PhpArray"/> with names of all defined classes (system and user).
		/// </summary>
		/// <returns><see cref="PhpArray"/> of class names.</returns>
		public static PhpArray get_declared_classes(Context ctx) => get_declared_types(ctx, false, false);

        /// <summary>
        /// Returns a <see cref="PhpArray"/> with names of all defined interfaces (system and user).
        /// </summary>
        /// <returns><see cref="PhpArray"/> of interface names.</returns>
        public static PhpArray get_declared_interfaces(Context ctx) => get_declared_types(ctx, true, false);

        /// <summary>
        /// Returns a <see cref="PhpArray"/> with names of all defined traits (system and user).
        /// </summary>
        /// <returns><see cref="PhpArray"/> of traits names.</returns>
        public static PhpArray get_declared_traits(Context ctx) => get_declared_types(ctx, false, true);

        /// <summary>
		/// Gets the name of the class from which class given by <paramref name="classNameOrObject"/>
		/// inherits.
		/// </summary>
        /// <remarks>
		/// If the class given by <paramref name="classNameOrObject"/> has no parent in PHP class hierarchy, this method returns <B>false</B>.
		/// </remarks>
		[return: CastToFalse]
        public static string get_parent_class(Context ctx, [ImportCallerClass]RuntimeTypeHandle caller, PhpValue classNameOrObject)
        {
            var tinfo = TypeNameOrObjectToType(ctx, classNameOrObject, caller);

            //
            var btype = tinfo?.BaseType;
            return btype?.Name;
        }

        /// <summary>
		/// Tests whether <paramref name="obj"/>'s class is derived from a class given by <paramref name="class_name"/>.
		/// </summary>
        /// <param name="ctx">Runtime context.</param>
        /// <param name="obj">The object to test.</param>
		/// <param name="class_name">The name of the class.</param>
        /// <returns><B>true</B> if the object <paramref name="obj"/> belongs to <paramref name="class_name"/> class or
		/// a class which is a subclass of <paramref name="class_name"/>, <B>false</B> otherwise.</returns>
        public static bool is_a(Context ctx, object obj, string class_name)
        {
            return obj != null && Core.Convert.IsInstanceOf(obj, ctx.GetDeclaredType(class_name));  // double check (obj!=null) for performance reasons
        }

        /// <summary>
		/// Tests whether <paramref name="value"/>'s class is derived from a class given by <paramref name="class_name"/>.
		/// </summary>
        /// <param name="ctx">Runtime context.</param>
        /// <param name="value">The object to test.</param>
		/// <param name="class_name">The name of the class.</param>
        /// <param name="allow_string">If this parameter set to FALSE, string class name as object is not allowed. This also prevents from calling autoloader if the class doesn't exist.</param>
        /// <returns><B>true</B> if the object <paramref name="value"/> belongs to <paramref name="class_name"/> class or
		/// a class which is a subclass of <paramref name="class_name"/>, <B>false</B> otherwise.</returns>
        public static bool is_a(Context ctx, PhpValue value, string class_name, bool allow_string)
        {
            // first load type of {value}
            PhpTypeInfo tvalue;
            var obj = value.AsObject();
            if (obj != null)
            {
                tvalue = obj.GetPhpTypeInfo();
            }
            else
            {
                var tname = value.AsString();
                if (tname != null && allow_string)
                {
                    // value can be a string specifying a class name
                    tvalue = ctx.GetDeclaredType(tname, true);
                }
                else
                {
                    // invalid argument type ignored
                    return false;
                }
            }

            // second, load type of {class_name}
            var ctype = ctx.GetDeclaredType(class_name, false);

            // check is_a
            return tvalue != null && ctype != null && tvalue.Type.IsSubclassOf(ctype.Type.AsType());
        }

        /// <summary>
        /// Gets the properties of the given object.
        /// </summary>
        /// <param name="caller">Caller context.</param>
        /// <param name="obj"></param>
        /// <returns>Returns an associative array of defined object accessible non-static properties for the specified object in scope.
        /// If a property has not been assigned a value, it will be returned with a NULL value.</returns>
        public static PhpArray get_object_vars([ImportCallerClass]RuntimeTypeHandle caller, object obj)
        {
            if (obj == null)
            {
                return null; // not FALSE since PHP 5.3
            }
            else if (obj.GetType() == typeof(stdClass))
            {
                // optimization for stdClass:
                var arr = ((stdClass)obj).GetRuntimeFields();
                return (arr != null) ? arr.DeepCopy() : PhpArray.NewEmpty();
            }
            else
            {
                var result = PhpArray.NewEmpty();

                foreach (var pair in TypeMembersUtils.EnumerateVisibleInstanceFields(obj, caller))
                {
                    result.Add(pair.Key, pair.Value.DeepCopy());
                }

                return result;
            }
        }

        /// <summary>
		/// Get <see cref="PhpTypeInfo"/> from either a class name or a class instance.
        /// </summary>
        /// <returns>Type info instance if object is valid class reference, otherwise <c>null</c>.</returns>
        internal static PhpTypeInfo TypeNameOrObjectToType(Context ctx, PhpValue @object, RuntimeTypeHandle selftype = default(RuntimeTypeHandle), bool autoload = true)
        {
            object obj;
            string str;

            if ((obj = (@object.AsObject())) != null)
            {
                return obj.GetType().GetPhpTypeInfo();
            }
            else if ((str = PhpVariable.AsString(@object)) != null)
            {
                return ctx.GetDeclaredType(str, autoload);
            }
            else
            {
                // other @object types are not handled
                return (selftype.Equals(default(RuntimeTypeHandle)) || !@object.IsNull)
                    ? null
                    : Type.GetTypeFromHandle(selftype)?.GetPhpTypeInfo();
            }
        }

        /// <summary>
        /// Get the default properties of the given class.
        /// </summary>
        /// <returns>Returns an associative array of declared properties visible from the current scope, with their default value. The resulting array elements are in the form of varname => value.
        /// In case of an error, it returns <c>FALSE</c>.</returns>
        [return: CastToFalse]
        public static PhpArray get_class_vars(Context ctx, [ImportCallerClass]RuntimeTypeHandle caller, string class_name)
        {
            var tinfo = ctx.GetDeclaredType(class_name, true);
            if (tinfo != null)
            {
                var result = new PhpArray();
                var callerType = Type.GetTypeFromHandle(caller);

                // the class has to be instantiated in order to discover default instance property values
                // (the constructor will initialize default properties, user defined constructor will not be called)
                var instanceOpt = tinfo.GetUninitializedInstance(ctx);

                foreach (var prop in tinfo.GetDeclaredProperties())
                {
                    if (prop.IsVisible(callerType))
                    {
                        // resolve the property value using temporary class instance
                        var value = prop.IsStatic
                            ? prop.GetValue(ctx, null)
                            : (instanceOpt != null)
                                ? prop.GetValue(ctx, instanceOpt)
                                : PhpValue.Void;

                        //
                        result[prop.PropertyName] = value.DeepCopy();
                    }
                }

                return result;
            }
            else
            {
                return null; // false
            }
        }

        /// <summary>
        /// Checks if the class method exists in the given object.
        /// </summary>
        /// <param name="ctx">Runtime context.</param>
        /// <param name="object">An object instance or a class name.</param>
        /// <param name="methodName">The method name.</param>
        /// <returns>Returns <c>TRUE</c> if the method given by <paramref name="methodName"/> has been defined for the given object, <c>FALSE</c> otherwise.</returns>
        public static bool method_exists(Context ctx, PhpValue @object, string methodName)
        {
            if (@object.IsEmpty || string.IsNullOrEmpty(methodName))
            {
                return false;
            }

            var tinfo = TypeNameOrObjectToType(ctx, @object);

            return tinfo != null && tinfo.RuntimeMethods[methodName] != null;
        }

        /// <summary>
		/// Verifies whether a property has been defined for the given object object or class. 
		/// </summary>
        /// <remarks>
		/// If an object is passed in the first parameter, the property is searched among runtime fields as well.
		/// </remarks>
		public static bool property_exists(Context ctx, PhpValue classNameOrObject, string propertyName)
        {
            var tinfo = TypeNameOrObjectToType(ctx, classNameOrObject);
            if (tinfo == null)
            {
                return false;
            }

            if (tinfo.GetDeclaredProperty(propertyName) != null)
            {
                // CT property found
                return true;
            }

            var instance = classNameOrObject.AsObject();
            if (instance != null && tinfo.GetRuntimeProperty(propertyName, instance) != null)
            {
                // RT property found
                return true;
            }

            //
            return false;
        }

        /// <summary>
		/// Returns all methods defined in the specified class or class of specified object, and its predecessors.
		/// </summary>
        /// <param name="ctx">Runtime context.</param>
        /// <param name="caller">The caller of the method to resolve visible properties properly. Can be UnknownTypeDesc.</param>
		/// <param name="classNameOrObject">The object (<see cref="DObject"/>) or the name of a class
		/// (<see cref="String"/>).</param>
		/// <returns>Array of all methods defined in <paramref name="classNameOrObject"/>.</returns>
		public static PhpArray get_class_methods(Context ctx, [ImportCallerClass]RuntimeTypeHandle caller, PhpValue classNameOrObject)
        {
            var tinfo = TypeNameOrObjectToType(ctx, classNameOrObject);
            if (tinfo == null)
            {
                return null;
            }

            //

            var result = new PhpArray();

            foreach (var m in tinfo.RuntimeMethods.EnumerateVisible(Type.GetTypeFromHandle(caller)))
            {
                result.Add((PhpValue)m.Name);
            }

            return result;
        }

        /// <summary>
        /// Creates an alias named <paramref name="alias"/> based on the user defined class <paramref name="original"/>.
        /// The aliased class is exactly the same as the original class.
        /// </summary>
        /// <param name="ctx">Runtime context.</param>
        /// <param name="original">Existing original class name.</param>
        /// <param name="alias">The alias name for the class.</param>
        /// <param name="autoload">Whether to autoload if the original class is not found. </param>
        /// <returns><c>true</c> on success.</returns>
        public static bool class_alias(Context ctx, string original, string alias, bool autoload = true)
        {
            if (!string.IsNullOrEmpty(original))
            {
                var type = ctx.GetDeclaredType(original, autoload);
                if (type != null && type.Name != alias)
                {
                    ctx.DeclareType(type, alias);
                    return ctx.GetDeclaredType(alias, false) == type;
                }
            }
            else
            {
                PhpException.InvalidArgument(nameof(original), LibResources.arg_null_or_empty);
            }

            return false;
        }
    }

    /// <summary>
    /// Container with SPL object functions.
    /// (they are listed as a part of SPL extensions)
    /// </summary>
    [PhpExtension(Spl.SplExtension.Name)]
    public static class ObjectsSpl
    {
        /// <summary>
		/// Returns a <see cref="PhpArray"/> with keys and values being names of a given class's
		/// base classes.
		/// </summary>
        /// <param name="ctx">Runtime context.</param>
        /// <param name="caller">The caller of the method used in case <paramref name="classNameOrObject"/> is NULL.</param>
        /// <param name="classNameOrObject">The object or class name to get base classes of.</param>
		/// <param name="useAutoload"><B>True</B> if autoloading should be used.</param>
		/// <returns>The <see cref="PhpArray"/> with base class names.</returns>
		[return: CastToFalse]
        public static PhpArray class_parents(Context ctx, [ImportCallerClass]RuntimeTypeHandle caller, PhpValue classNameOrObject, bool useAutoload = true)
        {
            var tinfo = Objects.TypeNameOrObjectToType(ctx, classNameOrObject, caller, useAutoload);

            PhpArray result = null;

            if (tinfo != null)
            {
                result = new PhpArray();
                while ((tinfo = tinfo.BaseType) != null)
                {
                    result.Add(tinfo.Name, tinfo.Name);
                }
            }

            return result;
        }

        /// <summary>
        /// This function returns an array with the names of the interfaces that the given class and its parents implement.
        /// </summary>
        [return: CastToFalse]
        public static PhpArray class_implements(Context ctx, [ImportCallerClass]RuntimeTypeHandle caller, PhpValue classNameOrObject, bool useAutoload = true)
        {
            PhpArray result = null;

            var tinfo = Objects.TypeNameOrObjectToType(ctx, classNameOrObject, caller, useAutoload);
            if (tinfo != null)
            {
                result = new PhpArray();
                foreach (var iface in tinfo.Type.ImplementedInterfaces)
                {
                    if (!iface.IsHiddenType())
                    {
                        result.Add(iface.Name, iface.Name);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// This function returns an array with the names of the traits that the given class uses.
        /// This does however not include any traits used by a parent class.
        /// </summary>
        [return: CastToFalse]
        public static PhpArray class_uses(Context ctx, [ImportCallerClass]RuntimeTypeHandle caller, PhpValue classNameOrObject, bool useAutoload = true)
        {
            PhpArray result = null;

            var tinfo = Objects.TypeNameOrObjectToType(ctx, classNameOrObject, caller, useAutoload);
            if (tinfo != null)
            {
                result = new PhpArray();
                //foreach (var trait in tinfo.Type.ImplementedTraits)
                //{
                //    if (!trait.IsHiddenType())
                //    {
                //        result.Add(trait.Name, trait.Name);
                //    }
                //}
                throw new NotImplementedException();
            }

            return result;
        }
    }
}
