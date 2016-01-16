using System.Collections.Generic;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace VSConstructorField
{
    [ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(VSConstructorFieldCodeRefactoringProvider)), Shared]
    internal class VSConstructorFieldCodeRefactoringProvider : CodeRefactoringProvider
    {
        public async override Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            var document = context.Document;

            if (document.Project.Solution.Workspace.Kind == WorkspaceKind.MiscellaneousFiles)
            {
                return;
            }

            var span = context.Span;
            if (!span.IsEmpty)
            {
                return;
            }

            var cancellationToken = context.CancellationToken;
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            var root = await model.SyntaxTree.GetRootAsync(cancellationToken).ConfigureAwait(false);
            var token = root.FindToken(span.Start);

            var parameter = token.Parent as ParameterSyntax;

            if (parameter == null)
            {
                var parameterList = token.Parent as ParameterListSyntax;
                if (parameterList != null)
                {
                    parameter = parameterList.Parameters.Last();
                }
            }

            if (parameter != null)
            {
                var ctor = parameter.Parent.Parent as ConstructorDeclarationSyntax;
                if (ctor == null)
                    return;

                context.RegisterRefactoring(
                    CodeAction.Create(
                        "Initialize field from parameter",
                        t2 =>
                        {
                            var newField = SyntaxFactory.FieldDeclaration(
                                SyntaxFactory.VariableDeclaration(
                                    parameter.Type,
                                    SyntaxFactory.SingletonSeparatedList<VariableDeclaratorSyntax>(
                                        SyntaxFactory.VariableDeclarator("_" + parameter.Identifier))
                                        )
                                    )
                                .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PrivateKeyword), SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword)))
                                .WithAdditionalAnnotations(Formatter.Annotation);

                            var assignmentStatement = SyntaxFactory.ExpressionStatement(
                                SyntaxFactory.AssignmentExpression(
                                    SyntaxKind.SimpleAssignmentExpression,
                                    SyntaxFactory.IdentifierName("_" + parameter.Identifier),
                                    SyntaxFactory.IdentifierName(parameter.Identifier)
                                )
                            ).WithAdditionalAnnotations(Formatter.Annotation);

                            var trackedRoot = root.TrackNodes(ctor);
                            var newRoot = trackedRoot.InsertNodesBefore(trackedRoot.GetCurrentNode(ctor), new List<SyntaxNode>() {
                                newField
                            });
                            newRoot = newRoot.ReplaceNode(
                                newRoot.GetCurrentNode(ctor), 
                                ctor.WithBody(
                                ctor.Body.WithStatements(
                                    SyntaxFactory.List<StatementSyntax>(new[] { assignmentStatement }.Concat(ctor.Body.Statements)))
                            ));

                            return Task.FromResult(document.WithSyntaxRoot(newRoot));
                        })
                );
            }
        }
    }
}