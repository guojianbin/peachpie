﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Roslyn.Utilities;
using System.Diagnostics;
using Pchp.CodeAnalysis.Semantics;
using Devsense.PHP.Syntax.Ast;
using Devsense.PHP.Syntax;

namespace Pchp.CodeAnalysis.Symbols
{
    /// <summary>
    /// PHP class as a CLR type.
    /// </summary>
    internal sealed partial class SourceNamedTypeSymbol : NamedTypeSymbol, IPhpTypeSymbol, IWithSynthesized
    {
        #region IPhpTypeSymbol

        /// <summary>
        /// Gets fully qualified name of the class.
        /// </summary>
        public QualifiedName FullName => _syntax.QualifiedName;

        /// <summary>
        /// Optional.
        /// A field holding a reference to current runtime context.
        /// Is of type <see cref="Pchp.Core.Context"/>.
        /// </summary>
        public FieldSymbol ContextStore
        {
            get
            {
                if (_lazyContextField == null && !this.IsStatic)
                {
                    // resolve <ctx> field
                    _lazyContextField = (this.BaseType as IPhpTypeSymbol)?.ContextStore;

                    //
                    if (_lazyContextField == null)
                    {
                        _lazyContextField = new SynthesizedFieldSymbol(this, DeclaringCompilation.CoreTypes.Context.Symbol, SpecialParameterSymbol.ContextName, Accessibility.Protected, false, true);
                    }
                }

                return _lazyContextField;
            }
        }

        /// <summary>
        /// Optional.
        /// A field holding array of the class runtime fields.
        /// Is of type <see cref="Pchp.Core.PhpArray"/>.
        /// </summary>
        public FieldSymbol RuntimeFieldsStore
        {
            get
            {
                if (_lazyRuntimeFieldsField == null && !this.IsStatic)
                {
                    const string fldname = "<runtime_fields>";

                    _lazyRuntimeFieldsField = (this.BaseType as IPhpTypeSymbol)?.RuntimeFieldsStore;

                    //
                    if (_lazyRuntimeFieldsField == null)
                    {
                        _lazyRuntimeFieldsField = new SynthesizedFieldSymbol(this, DeclaringCompilation.CoreTypes.PhpArray.Symbol, fldname, Accessibility.Internal, false);
                    }
                }

                return _lazyRuntimeFieldsField;
            }
        }

        /// <summary>
        /// Optional.
        /// A method <c>.phpnew</c> that ensures the initialization of the class without calling the base type constructor.
        /// </summary>
        public MethodSymbol InitializeInstanceMethod => this.IsStatic ? null : (_lazyPhpNewMethod ?? (_lazyPhpNewMethod = new SynthesizedPhpNewMethodSymbol(this)));

        /// <summary>
        /// Optional.
        /// A nested class <c>__statics</c> containing class static fields and constants which are bound to runtime context.
        /// </summary>
        public NamedTypeSymbol StaticsContainer
        {
            get
            {
                if (_lazyStaticsContainer == null)
                {
                    _lazyStaticsContainer = new SynthesizedStaticFieldsHolder(this);
                }

                return _lazyStaticsContainer;
            }
        }

        #endregion

        readonly TypeDecl _syntax;
        readonly SourceFileSymbol _file;

        ImmutableArray<Symbol> _lazyMembers;

        NamedTypeSymbol _lazyBaseType;
        MethodSymbol _lazyCtorMethod, _lazyPhpNewMethod;   // .ctor, .phpnew 
        SynthesizedCctorSymbol _lazyCctorSymbol;   // .cctor
        FieldSymbol _lazyContextField;   // protected Pchp.Core.Context <ctx>;
        FieldSymbol _lazyRuntimeFieldsField; // internal Pchp.Core.PhpArray <runtimeFields>;
        SynthesizedStaticFieldsHolder _lazyStaticsContainer; // class __statics { ... }
        SynthesizedMethodSymbol _lazyInvokeSymbol; // IPhpCallable.Invoke(Context, PhpValue[]);

        public SourceFileSymbol ContainingFile => _file;

        public SourceNamedTypeSymbol(SourceFileSymbol file, TypeDecl syntax)
        {
            Contract.ThrowIfNull(file);

            _syntax = syntax;
            _file = file;
        }

        ImmutableArray<Symbol> Members()
        {
            if (_lazyMembers.IsDefault)
            {
                var members = new List<Symbol>();

                //
                if (StaticsContainer.GetMembers().Any())
                {
                    members.Add(StaticsContainer);
                }

                //
                members.AddRange(LoadMethods());
                members.AddRange(LoadFields());

                //
                _lazyMembers = members.AsImmutable();
            }

            return _lazyMembers;
        }

        IEnumerable<MethodSymbol> LoadMethods()
        {
            // source methods
            foreach (var m in _syntax.Members.OfType<MethodDecl>())
            {
                yield return new SourceMethodSymbol(this, m);
            }

            // .ctor
            if (PhpCtorMethodSymbol != null)
            {
                yield return PhpCtorMethodSymbol;
            }

            // .phpnew
            if (InitializeInstanceMethod != null)
            {
                yield return InitializeInstanceMethod;
            }

            // ..ctor
            if (_lazyCctorSymbol != null)
            {
                yield return _lazyCctorSymbol;
            }
        }

        IEnumerable<FieldSymbol> LoadFields()
        {
            var binder = new SemanticsBinder(null, null);

            var runtimestatics = StaticsContainer;

            // source fields
            foreach (var flist in _syntax.Members.OfType<FieldDeclList>())
            {
                foreach (var f in flist.Fields)
                {
                    if (runtimestatics.GetMembers(f.Name.Value).IsEmpty)
                        yield return new SourceFieldSymbol(this, f.Name.Value, flist.Modifiers, flist.PHPDoc,
                            f.HasInitVal ? binder.BindExpression(f.Initializer, BoundAccess.Read) : null);
                }
            }

            // source constants
            foreach (var clist in _syntax.Members.OfType<ConstDeclList>())
            {
                foreach (var c in clist.Constants)
                {
                    if (runtimestatics.GetMembers(c.Name.Name.Value).IsEmpty)
                        yield return new SourceConstSymbol(this, c.Name.Name.Value, clist.PHPDoc, c.Initializer);
                }
            }

            // special fields
            if (ContextStore != null && object.ReferenceEquals(ContextStore.ContainingType, this))
                yield return ContextStore;

            var runtimefld = RuntimeFieldsStore;
            if (runtimefld != null && object.ReferenceEquals(runtimefld.ContainingType, this))
                yield return runtimefld;
        }

        /// <summary>
        /// <c>.ctor</c> synthesized method. Only if type is not static.
        /// </summary>
        internal MethodSymbol PhpCtorMethodSymbol => this.IsStatic ? null : (_lazyCtorMethod ?? (_lazyCtorMethod = new SynthesizedPhpCtorSymbol(this)));

        public override ImmutableArray<MethodSymbol> StaticConstructors
        {
            get
            {
                if (_lazyCctorSymbol != null)
                    return ImmutableArray.Create<MethodSymbol>(_lazyCctorSymbol);

                return ImmutableArray<MethodSymbol>.Empty;
            }
        }

        /// <summary>
        /// In case the class implements <c>__invoke</c> method, we create special Invoke() method that is compatible with IPhpCallable interface.
        /// </summary>
        internal SynthesizedMethodSymbol EnsureInvokeMethod()
        {
            if (_lazyInvokeSymbol == null)
            {
                if (GetMembers(Devsense.PHP.Syntax.Name.SpecialMethodNames.Invoke.Value).Any(s => s is MethodSymbol))
                {
                    _lazyInvokeSymbol = new SynthesizedMethodSymbol(this, "IPhpCallable.Invoke", false, true, DeclaringCompilation.CoreTypes.PhpValue)
                    {
                        ExplicitOverride = (MethodSymbol)DeclaringCompilation.CoreTypes.IPhpCallable.Symbol.GetMembers("Invoke").Single(),
                    };
                    _lazyInvokeSymbol.SetParameters(
                        new SpecialParameterSymbol(_lazyInvokeSymbol, DeclaringCompilation.CoreTypes.Context, SpecialParameterSymbol.ContextName, 0),
                        new SpecialParameterSymbol(_lazyInvokeSymbol, ArrayTypeSymbol.CreateSZArray(ContainingAssembly, DeclaringCompilation.CoreTypes.PhpValue.Symbol), "arguments", 1));

                    _lazyMembers = _lazyMembers.Add(_lazyInvokeSymbol);
                }
            }
            return _lazyInvokeSymbol;
        }

        public override NamedTypeSymbol BaseType
        {
            get
            {
                if (_lazyBaseType == null)
                {
                    if (_syntax.BaseClass != null)
                    {
                        _lazyBaseType = (NamedTypeSymbol)DeclaringCompilation.GetTypeByMetadataName(_syntax.BaseClass.ClassName.ClrName())
                            ?? new MissingMetadataTypeSymbol(_syntax.BaseClass.ClassName.ClrName(), 0, false);

                        if (_lazyBaseType.Arity != 0)
                        {
                            throw new NotImplementedException();    // generics
                        }
                    }
                    else
                    {
                        _lazyBaseType = DeclaringCompilation.CoreTypes.Object.Symbol;
                    }
                }

                return _lazyBaseType;
            }
        }

        /// <summary>
        /// Gets type declaration syntax node.
        /// </summary>
        internal TypeDecl Syntax => _syntax;

        public override int Arity => 0;

        internal override IModuleSymbol ContainingModule => _file.SourceModule;

        public override Symbol ContainingSymbol => _file.SourceModule;

        internal override PhpCompilation DeclaringCompilation => _file.DeclaringCompilation;

        public override string Name => _syntax.Name.Name.Value;

        public override string NamespaceName
            => (_syntax.ContainingNamespace != null) ? _syntax.ContainingNamespace.QualifiedName.QualifiedName.ClrName() : string.Empty;

        public override string MetadataName
        {
            get
            {
                var name = base.MetadataName;

                if (_syntax.IsConditional)
                {
                    var ambiguities = this.DeclaringCompilation.SourceSymbolTables.GetTypes().Where(t => t.Name == this.Name && t.NamespaceName == this.NamespaceName);
                    name += "@" + ambiguities.TakeWhile(f => f != this).Count().ToString(); // index within types with the same name
                }

                return name;
            }
        }

        public override TypeKind TypeKind
        {
            get
            {
                return IsInterface ? TypeKind.Interface : TypeKind.Class;
            }
        }

        public override Accessibility DeclaredAccessibility => _syntax.MemberAttributes.GetAccessibility();

        public override ImmutableArray<SyntaxReference> DeclaringSyntaxReferences
        {
            get
            {
                return ImmutableArray<SyntaxReference>.Empty;
            }
        }

        internal override bool IsInterface => (_syntax.MemberAttributes & PhpMemberAttributes.Interface) != 0;

        public override bool IsAbstract => _syntax.MemberAttributes.IsAbstract();

        public override bool IsSealed => _syntax.MemberAttributes.IsSealed();

        public override bool IsStatic => _syntax.MemberAttributes.IsStatic();

        public override ImmutableArray<Location> Locations
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        internal override bool ShouldAddWinRTMembers => false;

        internal override bool IsWindowsRuntimeImport => false;

        internal override TypeLayout Layout => default(TypeLayout);

        internal override ObsoleteAttributeData ObsoleteAttributeData
        {
            get
            {
                return null;
            }
        }

        internal override bool MangleName => false;

        public override ImmutableArray<NamedTypeSymbol> Interfaces => GetDeclaredInterfaces(null);

        public override ImmutableArray<Symbol> GetMembers() => Members();

        public override ImmutableArray<Symbol> GetMembers(string name)
            => Members().Where(s => s.Name.EqualsOrdinalIgnoreCase(name)).AsImmutable();

        public override ImmutableArray<NamedTypeSymbol> GetTypeMembers() => _lazyMembers.OfType<NamedTypeSymbol>().AsImmutable();

        public override ImmutableArray<NamedTypeSymbol> GetTypeMembers(string name) => _lazyMembers.OfType<NamedTypeSymbol>().Where(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).AsImmutable();

        internal override IEnumerable<IFieldSymbol> GetFieldsToEmit()
        {
            return Members().OfType<IFieldSymbol>();
        }

        internal override ImmutableArray<NamedTypeSymbol> GetInterfacesToEmit()
        {
            return this.Interfaces;
        }

        internal override ImmutableArray<NamedTypeSymbol> GetDeclaredInterfaces(ConsList<Symbol> basesBeingResolved)
        {
            var ifaces = new HashSet<NamedTypeSymbol>();
            foreach (var i in _syntax.ImplementsList)
            {
                var t = (NamedTypeSymbol)DeclaringCompilation.GetTypeByMetadataName(i.ClassName.ClrName())
                        ?? new MissingMetadataTypeSymbol(i.ClassName.ClrName(), 0, false);

                if (t.Arity != 0)
                {
                    throw new NotImplementedException();    // generics
                }

                ifaces.Add(t);
            }

            // __invoke => IPhpCallable
            if (EnsureInvokeMethod() != null)
            {
                ifaces.Add(DeclaringCompilation.CoreTypes.IPhpCallable);
            }

            //
            return ifaces.AsImmutable();
        }

        MethodSymbol IWithSynthesized.GetOrCreateStaticCtorSymbol()
        {
            if (_lazyCctorSymbol == null)
            {
                _lazyCctorSymbol = new SynthesizedCctorSymbol(this);

                if (!_lazyMembers.IsDefault)
                    _lazyMembers = _lazyMembers.Add(_lazyCctorSymbol);
            }

            return _lazyCctorSymbol;
        }

        SynthesizedFieldSymbol IWithSynthesized.GetOrCreateSynthesizedField(TypeSymbol type, string name, Accessibility accessibility, bool isstatic, bool @readonly)
        {
            GetMembers();

            var field = _lazyMembers.OfType<SynthesizedFieldSymbol>().FirstOrDefault(f => f.Name == name && f.IsStatic == isstatic && f.Type == type && f.IsReadOnly == @readonly);
            if (field == null)
            {
                field = new SynthesizedFieldSymbol(this, type, name, accessibility, isstatic, @readonly);
                _lazyMembers = _lazyMembers.Add(field);
            }

            return field;
        }

        void IWithSynthesized.AddTypeMember(NamedTypeSymbol nestedType)
        {
            Contract.ThrowIfNull(nestedType);

            _lazyMembers = _lazyMembers.Add(nestedType);
        }
    }
}