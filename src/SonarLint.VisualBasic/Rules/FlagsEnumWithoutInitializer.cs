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
using Microsoft.CodeAnalysis.Diagnostics;
using SonarLint.Common;
using SonarLint.Common.Sqale;
using SonarLint.Rules.Common;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using SonarLint.Helpers;

namespace SonarLint.Rules.VisualBasic
{
    [DiagnosticAnalyzer(LanguageNames.VisualBasic)]
    [SqaleConstantRemediation("5min")]
    [Rule(DiagnosticId, RuleSeverity, Title, IsActivatedByDefault)]
    [SqaleSubCharacteristic(SqaleSubCharacteristic.DataReliability)]
    [Tags(Tag.Bug)]
    public class FlagsEnumWithoutInitializer : FlagsEnumWithoutInitializerBase<SyntaxKind, EnumStatementSyntax>
    {
        private static readonly ImmutableArray<SyntaxKind> kindsOfInterest = ImmutableArray.Create(SyntaxKind.EnumStatement);
        public override ImmutableArray<SyntaxKind> SyntaxKindsOfInterest => kindsOfInterest;

        protected override bool AllMembersAreInitialized(EnumStatementSyntax declaration)
        {
            var parent = declaration.Parent as EnumBlockSyntax;
            if (parent == null)
            {
                return true;
            }
            return parent.ChildNodes().OfType<EnumMemberDeclarationSyntax>().All(member => member.Initializer != null);
        }

        protected override SyntaxToken GetIdentifier(EnumStatementSyntax declaration) => declaration.Identifier;

        protected sealed override GeneratedCodeRecognizer GeneratedCodeRecognizer => Helpers.VisualBasic.GeneratedCodeRecognizer.Instance;
    }
}
