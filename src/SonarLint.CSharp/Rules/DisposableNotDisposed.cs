﻿/*
 * SonarLint for Visual Studio
 * Copyright (C) 2015-2016 SonarSource SA
 * mailto:contact@sonarsource.com
 *
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 3 of the License, or (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public
 * License along with this program; if not, write to the Free Software
 * Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02
 */

using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SonarLint.Common;
using SonarLint.Common.Sqale;
using SonarLint.Helpers;
using System.Collections.Generic;

namespace SonarLint.Rules.CSharp
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    [SqaleSubCharacteristic(SqaleSubCharacteristic.LogicReliability)]
    [SqaleConstantRemediation("10min")]
    [Rule(DiagnosticId, RuleSeverity, Title, IsActivatedByDefault)]
    [Tags(Tag.Bug, Tag.Cwe, Tag.DenialOfService, Tag.Security)]
    public class DisposableNotDisposed : SonarDiagnosticAnalyzer
    {
        internal const string DiagnosticId = "S2930";
        internal const string Title = "\"IDisposables\" should be disposed";
        internal const string MessageFormat = "\"Dispose\" of \"{0}\".";
        internal const string Category = SonarLint.Common.Category.Reliability;
        internal const Severity RuleSeverity = Severity.Critical;
        internal const bool IsActivatedByDefault = true;

        internal static readonly DiagnosticDescriptor Rule =
            new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category,
                RuleSeverity.ToDiagnosticSeverity(), IsActivatedByDefault,
                helpLinkUri: DiagnosticId.GetHelpLink());

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule); } }

        private static readonly ISet<KnownType> TrackedTypes = new []
        {
            KnownType.System_IO_FileStream,
            KnownType.System_IO_StreamReader,
            KnownType.System_IO_StreamWriter,

            KnownType.System_Net_WebClient,

            KnownType.System_Net_Sockets_TcpClient,
            KnownType.System_Net_Sockets_TcpListener,
            KnownType.System_Net_Sockets_UdpClient,

            KnownType.System_Drawing_Image,
            KnownType.System_Drawing_Bitmap
        }.ToImmutableHashSet();

        private static readonly ISet<string> DisposeMethods = new []
        {
            "Dispose",
            "Close"
        }.ToImmutableHashSet();

        private static readonly ISet<string> FactoryMethods = new []
        {
            "System.IO.File.Create",
            "System.IO.File.Open",
            "System.Drawing.Image.FromFile",
            "System.Drawing.Image.FromStream"
        }.ToImmutableHashSet();

        private class NodeAndSymbol
        {
            public SyntaxNode Node { get; set; }
            public ISymbol Symbol { get; set; }
        }

        protected override void Initialize(SonarAnalysisContext context)
        {
            context.RegisterSymbolAction(
                c =>
                {
                    var namedType = (INamedTypeSymbol)c.Symbol;
                    if (namedType.ContainingType != null ||
                        !namedType.IsClassOrStruct())
                    {
                        return;
                    }

                    var typesDeclarationsAndSemanticModels =
                        namedType.DeclaringSyntaxReferences
                        .Select(r => new UnusedPrivateMember.SyntaxNodeWithSemanticModel<SyntaxNode>
                        {
                            SyntaxNode = r.GetSyntax(),
                            SemanticModel = c.Compilation.GetSemanticModel(r.SyntaxTree)
                        });

                    var trackedNodesAndSymbols = new HashSet<NodeAndSymbol>();
                    foreach (var typeDeclarationAndSemanticModel in typesDeclarationsAndSemanticModels)
                    {
                        TrackInitializedLocalsAndPrivateFields(typeDeclarationAndSemanticModel.SyntaxNode, typeDeclarationAndSemanticModel.SemanticModel, trackedNodesAndSymbols);
                        TrackAssignmentsToLocalsAndPrivateFields(typeDeclarationAndSemanticModel.SyntaxNode, typeDeclarationAndSemanticModel.SemanticModel, trackedNodesAndSymbols);
                    }

                    if (trackedNodesAndSymbols.Any())
                    {
                        var excludedSymbols = new HashSet<ISymbol>();
                        foreach (var typeDeclarationAndSemanticModel in typesDeclarationsAndSemanticModels)
                        {
                            ExcludeDisposedAndClosedLocalsAndPrivateFields(typeDeclarationAndSemanticModel.SyntaxNode, typeDeclarationAndSemanticModel.SemanticModel, excludedSymbols);
                            ExcludeReturnedPassedAndAliasedLocalsAndPrivateFields(typeDeclarationAndSemanticModel.SyntaxNode, typeDeclarationAndSemanticModel.SemanticModel, excludedSymbols);
                        }

                        foreach (var trackedNodeAndSymbol in trackedNodesAndSymbols)
                        {
                            if (!excludedSymbols.Contains(trackedNodeAndSymbol.Symbol))
                            {
                                c.ReportDiagnosticIfNonGenerated(Diagnostic.Create(Rule, trackedNodeAndSymbol.Node.GetLocation(), trackedNodeAndSymbol.Symbol.Name), c.Compilation);
                            }
                        }
                    }
                },
                SymbolKind.NamedType);
        }

        private static void TrackInitializedLocalsAndPrivateFields(SyntaxNode typeDeclaration, SemanticModel semanticModel, ISet<NodeAndSymbol> trackedNodesAndSymbols)
        {
            var localAndFieldDeclarations = typeDeclaration
                .DescendantNodes()
                .Where(n => n.IsKind(SyntaxKind.LocalDeclarationStatement) || n.IsKind(SyntaxKind.FieldDeclaration));

            foreach (var localOrFieldDeclaration in localAndFieldDeclarations)
            {
                VariableDeclarationSyntax declaration;
                if (localOrFieldDeclaration.IsKind(SyntaxKind.LocalDeclarationStatement))
                {
                    declaration = ((LocalDeclarationStatementSyntax)localOrFieldDeclaration).Declaration;
                }
                else if (localOrFieldDeclaration.IsKind(SyntaxKind.FieldDeclaration))
                {
                    var fieldDeclaration = (FieldDeclarationSyntax)localOrFieldDeclaration;
                    if (fieldDeclaration.Modifiers.Any() && !fieldDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PrivateKeyword)))
                    {
                        continue;
                    }

                    declaration = fieldDeclaration.Declaration;
                }
                else
                {
                    throw new NotSupportedException("Syntax node should be either a local declaration or a field declaration");
                }

                foreach (var variableNode in declaration.Variables.Where(v => v.Initializer != null && IsInstantiation(v.Initializer.Value, semanticModel)))
                {
                    trackedNodesAndSymbols.Add(new NodeAndSymbol { Node = variableNode, Symbol = semanticModel.GetDeclaredSymbol(variableNode) });
                }
            }
        }

        private static void TrackAssignmentsToLocalsAndPrivateFields(SyntaxNode typeDeclaration, SemanticModel semanticModel, ISet<NodeAndSymbol> trackedNodesAndSymbols)
        {
            var simpleAssignments = typeDeclaration
                .DescendantNodes()
                .Where(n => n.IsKind(SyntaxKind.SimpleAssignmentExpression))
                .Cast<AssignmentExpressionSyntax>();

            foreach (var simpleAssignment in simpleAssignments)
            {
                if (simpleAssignment.Parent.IsKind(SyntaxKind.UsingStatement))
                {
                    continue;
                }

                if (!IsInstantiation(simpleAssignment.Right, semanticModel))
                {
                    continue;
                }

                var referencedSymbol = semanticModel.GetSymbolInfo(simpleAssignment.Left).Symbol;
                if (referencedSymbol != null && IsLocalOrPrivateField(referencedSymbol))
                {
                    trackedNodesAndSymbols.Add(new NodeAndSymbol { Node = simpleAssignment, Symbol = referencedSymbol });
                }
            }
        }

        private static bool IsLocalOrPrivateField(ISymbol symbol)
        {
            return symbol.Kind == SymbolKind.Local ||
                (symbol.Kind == SymbolKind.Field && symbol.DeclaredAccessibility == Accessibility.Private);
        }

        private static void ExcludeDisposedAndClosedLocalsAndPrivateFields(SyntaxNode typeDeclaration, SemanticModel semanticModel, ISet<ISymbol> excludedSymbols)
        {
            var incovationsAndConditionalAccesses = typeDeclaration
                .DescendantNodes()
                .Where(n => n.IsKind(SyntaxKind.InvocationExpression) || n.IsKind(SyntaxKind.ConditionalAccessExpression));

            foreach (var incovationOrConditionalAccess in incovationsAndConditionalAccesses)
            {
                SimpleNameSyntax name;
                ExpressionSyntax expression;

                if (incovationOrConditionalAccess.IsKind(SyntaxKind.InvocationExpression))
                {
                    var invocation = (InvocationExpressionSyntax)incovationOrConditionalAccess;
                    var memberAccessNode = invocation.Expression as MemberAccessExpressionSyntax;

                    name = memberAccessNode?.Name;
                    expression = memberAccessNode?.Expression;
                }
                else if (incovationOrConditionalAccess.IsKind(SyntaxKind.ConditionalAccessExpression))
                {
                    var conditionalAccess = (ConditionalAccessExpressionSyntax)incovationOrConditionalAccess;
                    var invocation = conditionalAccess.WhenNotNull as InvocationExpressionSyntax;
                    if (invocation == null)
                    {
                        continue;
                    }

                    var memberBindingNode = invocation.Expression as MemberBindingExpressionSyntax;

                    name = memberBindingNode?.Name;
                    expression = conditionalAccess.Expression;
                }
                else
                {
                    throw new NotSupportedException("Syntax node should be either an invocation or a conditional access expression");
                }

                if (name == null || !DisposeMethods.Contains(name.Identifier.Text))
                {
                    continue;
                }

                var referencedSymbol = semanticModel.GetSymbolInfo(expression).Symbol;
                if (referencedSymbol != null && IsLocalOrPrivateField(referencedSymbol))
                {
                    excludedSymbols.Add(referencedSymbol);
                }
            }
        }

        private static void ExcludeReturnedPassedAndAliasedLocalsAndPrivateFields(SyntaxNode typeDeclaration, SemanticModel semanticModel, ISet<ISymbol> excludedSymbols)
        {
            var identifiersAndSimpleMemberAccesses = typeDeclaration
                .DescendantNodes()
                .Where(n => n.IsKind(SyntaxKind.IdentifierName) || n.IsKind(SyntaxKind.SimpleMemberAccessExpression));

            foreach (var identifierOrSimpleMemberAccess in identifiersAndSimpleMemberAccesses)
            {
                ExpressionSyntax expression;
                if (identifierOrSimpleMemberAccess.IsKind(SyntaxKind.IdentifierName))
                {
                    expression = (IdentifierNameSyntax)identifierOrSimpleMemberAccess;
                }
                else if (identifierOrSimpleMemberAccess.IsKind(SyntaxKind.SimpleMemberAccessExpression))
                {

                    var memberAccess = (MemberAccessExpressionSyntax)identifierOrSimpleMemberAccess;
                    if (!memberAccess.Expression.IsKind(SyntaxKind.ThisExpression))
                    {
                        continue;
                    }
                    expression = memberAccess;
                }
                else
                {
                    throw new NotSupportedException("Syntax node should be either an identifier or a simple member access expression");
                }

                if (IsStandaloneExpression(expression))
                {
                    var referencedSymbol = semanticModel.GetSymbolInfo(identifierOrSimpleMemberAccess).Symbol;
                    if (referencedSymbol != null && IsLocalOrPrivateField(referencedSymbol))
                    {
                        excludedSymbols.Add(referencedSymbol);
                    }
                }
            }
        }

        private static bool IsStandaloneExpression(ExpressionSyntax expression)
        {
            var parentAsAssignment = expression.Parent as AssignmentExpressionSyntax;

            return !(expression.Parent is ExpressionSyntax) ||
                (parentAsAssignment != null && object.ReferenceEquals(expression, parentAsAssignment.Right));
        }

        private static bool IsInstantiation(ExpressionSyntax expression, SemanticModel semanticModel)
        {
            return IsNewTrackedTypeObjectCreation(expression, semanticModel) ||
                IsFactoryMethodInvocation(expression, semanticModel);
        }

        private static bool IsNewTrackedTypeObjectCreation(ExpressionSyntax expression, SemanticModel semanticModel)
        {
            if (!expression.IsKind(SyntaxKind.ObjectCreationExpression))
            {
                return false;
            }

            var type = semanticModel.GetTypeInfo(expression).Type;
            if (!type.IsAny(TrackedTypes))
            {
                return false;
            }

            var constructor = semanticModel.GetSymbolInfo(expression).Symbol as IMethodSymbol;
            return constructor != null && !constructor.Parameters.Any(p => p.Type.Implements(KnownType.System_IDisposable));
        }

        private static bool IsFactoryMethodInvocation(ExpressionSyntax expression, SemanticModel semanticModel)
        {
            var invocation = expression as InvocationExpressionSyntax;
            if (invocation == null)
            {
                return false;
            }

            var methodSymbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (methodSymbol == null)
            {
                return false;
            }

            var methodQualifiedName = methodSymbol.ContainingType.ToDisplayString() + "." + methodSymbol.Name;
            return FactoryMethods.Contains(methodQualifiedName);
        }
    }
}
