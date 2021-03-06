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

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SonarLint.Common;
using SonarLint.Common.Sqale;
using SonarLint.Helpers;

namespace SonarLint.Rules.VisualBasic
{
    [DiagnosticAnalyzer(LanguageNames.VisualBasic)]
    [SqaleConstantRemediation("2min")]
    [SqaleSubCharacteristic(SqaleSubCharacteristic.Readability)]
    [Rule(DiagnosticId, RuleSeverity, Title, IsActivatedByDefault)]
    [Tags(Tag.Clumsy)]
    public class ArrayCreationLongSyntax : SonarDiagnosticAnalyzer
    {
        internal const string DiagnosticId = "S2355";
        internal const string Title = "Array literals should be used instead of array creation expressions";
        internal const string Description =
            "Array literals are more compact than array creation expressions.";
        internal const string MessageFormat = "Use an array literal here instead.";
        internal const string Category = SonarLint.Common.Category.Maintainability;
        internal const Severity RuleSeverity = Severity.Major;
        internal const bool IsActivatedByDefault = true;

        internal static readonly DiagnosticDescriptor Rule =
            new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category,
                RuleSeverity.ToDiagnosticSeverity(), IsActivatedByDefault,
                helpLinkUri: DiagnosticId.GetHelpLink(),
                description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule); } }

        protected override void Initialize(SonarAnalysisContext context)
        {
            context.RegisterSyntaxNodeActionInNonGenerated(
                c =>
                {
                    var arrayCreation = (ArrayCreationExpressionSyntax)c.Node;
                    if (arrayCreation.Initializer == null)
                    {
                        return;
                    }

                    var arrayType = c.SemanticModel.GetTypeInfo(arrayCreation).Type as IArrayTypeSymbol;
                    if (arrayType == null ||
                        arrayType.ElementType == null ||
                        arrayType.ElementType is IErrorTypeSymbol)
                    {
                        return;
                    }

                    if (arrayType.ElementType.Is(KnownType.System_Object) &&
                        !arrayCreation.Initializer.Initializers.Any())
                    {
                        c.ReportDiagnostic(Diagnostic.Create(Rule, arrayCreation.GetLocation()));
                        return;
                    }

                    if (!arrayCreation.Initializer.Initializers.Any())
                    {
                        return;
                    }

                    if (AtLeastOneExactTypeMatch(c.SemanticModel, arrayCreation, arrayType) &&
                        AllTypesAreConvertible(c.SemanticModel, arrayCreation, arrayType))
                    {
                        c.ReportDiagnostic(Diagnostic.Create(Rule, arrayCreation.GetLocation()));
                    }
                },
                SyntaxKind.ArrayCreationExpression);
        }

        private static bool AllTypesAreConvertible(SemanticModel semanticModel, ArrayCreationExpressionSyntax arrayCreation, IArrayTypeSymbol arrayType)
        {
            return arrayCreation.Initializer.Initializers.All(initializer =>
            {
                var conversion = semanticModel.ClassifyConversion(initializer, arrayType.ElementType);
                return conversion.Exists && (conversion.IsIdentity || conversion.IsWidening);
            });
        }

        private static bool AtLeastOneExactTypeMatch(SemanticModel semanticModel, ArrayCreationExpressionSyntax arrayCreation, IArrayTypeSymbol arrayType)
        {
            return arrayCreation.Initializer.Initializers.Any(initializer => arrayType.ElementType.Equals(semanticModel.GetTypeInfo(initializer).Type));
        }
    }
}
