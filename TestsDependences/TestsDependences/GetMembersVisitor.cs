﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Roslyn.Compilers.Common;
using Roslyn.Compilers.CSharp;

namespace TestsDependences
{
    partial class GetMembersVisitor : SyntaxVisitor
    {
        private const int MaxFailed = 50;

        private bool _proceed;
        private ISemanticModel _semanticModel;
        private SpecificationsElements _specificationsElements;
        private string _serverFxDALInterfacesNamespace;
        private Dictionary<MethodDeclarationSyntax, ISemanticModel> _semanticModelPerMethods;
        private Dictionary<string, MethodDeclarationSyntax> _methodPerMethodSymbols;
        private List<MethodDeclarationSyntax> _extensionMethods;
        private Dictionary<string, List<MethodDeclarationSyntax>> _getMethods;
        private Dictionary<string, PropertyDependence> _variables;
        private Dictionary<string, int> _fromVariables;
        private int _linqIndex;
        private List<MethodSymbol> _alreadyCalledMethods;
        private bool _fromOriginalMethod;
        private bool _definePropertyDependences;
        private PropertyDependence _returnProperties;
        private PropertyDependence _properties;
        private int _failed = 0;

        public GetMembersVisitor(ISemanticModel semanticModel, SpecificationsElements specificationsElements,
            MethodSymbol methodSymbol, string variableName, string serverFxDALInterfacesNamespace,
            Dictionary<MethodDeclarationSyntax, ISemanticModel> semanticModelPerMethods,
            Dictionary<string, MethodDeclarationSyntax> methodPerMethodSymbols,
            Dictionary<string, List<MethodDeclarationSyntax>> getMethods, List<MethodDeclarationSyntax> extensionMethods)
            : this(
                semanticModel, specificationsElements, serverFxDALInterfacesNamespace, semanticModelPerMethods,
                methodPerMethodSymbols, getMethods, extensionMethods,
                new Dictionary<string, PropertyDependence>() { { variableName, null } },
                new Dictionary<string, int>(), 0, new List<MethodSymbol>() { methodSymbol }, false)
        {
            _fromOriginalMethod = true;
        }

        public GetMembersVisitor(ISemanticModel semanticModel, SpecificationsElements specificationsElements,
            MethodSymbol methodSymbol, string serverFxDALInterfacesNamespace,
            Dictionary<MethodDeclarationSyntax, ISemanticModel> semanticModelPerMethods,
            Dictionary<string, MethodDeclarationSyntax> methodPerMethodSymbols,
            Dictionary<string, List<MethodDeclarationSyntax>> getMethods, List<MethodDeclarationSyntax> extensionMethods)
            : this(
                semanticModel, specificationsElements, serverFxDALInterfacesNamespace, semanticModelPerMethods,
                methodPerMethodSymbols, getMethods, extensionMethods,
                new Dictionary<string, PropertyDependence>(), new Dictionary<string, int>(), 0,
                new List<MethodSymbol>() { methodSymbol }, true)
        {
            _fromOriginalMethod = true;
        }

        private GetMembersVisitor(GetMembersVisitor @base,
            Dictionary<string, PropertyDependence> variables = null)
            : this(
                @base._semanticModel, @base._specificationsElements, @base._serverFxDALInterfacesNamespace,
                @base._semanticModelPerMethods, @base._methodPerMethodSymbols, @base._getMethods,
                @base._extensionMethods, variables ?? @base._variables, @base._fromVariables, @base._linqIndex,
                @base._alreadyCalledMethods, @base._definePropertyDependences, @base._failed)
        {
            _fromOriginalMethod = false;
            _returnProperties = @base._returnProperties;
        }

        private GetMembersVisitor(ISemanticModel semanticModel, SpecificationsElements specificationsElements,
            string serverFxDALInterfacesNamespace,
            Dictionary<MethodDeclarationSyntax, ISemanticModel> semanticModelPerMethods,
            Dictionary<string, MethodDeclarationSyntax> methodPerMethodSymbols,
            Dictionary<string, List<MethodDeclarationSyntax>> getMethods, List<MethodDeclarationSyntax> extensionMethods,
            Dictionary<string, PropertyDependence> variables, Dictionary<string, int> fromVariables,
            int linqIndex, List<MethodSymbol> alreadyCalledMethods, bool definePropertyDependences, int failed = 0)
        {
            _semanticModel = semanticModel;
            _specificationsElements = specificationsElements;
            _serverFxDALInterfacesNamespace = serverFxDALInterfacesNamespace;
            _semanticModelPerMethods = semanticModelPerMethods;
            _methodPerMethodSymbols = methodPerMethodSymbols;
            _getMethods = getMethods;
            _extensionMethods = extensionMethods;
            _variables = variables;
            _fromVariables = fromVariables;
            _linqIndex = linqIndex;
            _alreadyCalledMethods = alreadyCalledMethods;
            _properties = new PropertyDependence();
            _definePropertyDependences = definePropertyDependences;
            _failed = failed;
        }

        public List<List<PropertySymbolInfo>> GetProperties()
        {
            return GetProperties(_properties);
        }

        public List<List<PropertySymbolInfo>> GetReturnProperties()
        {
            if (_returnProperties == null)
                return null;
            return GetProperties(_returnProperties);
        }

        private List<List<PropertySymbolInfo>> GetProperties(PropertyDependence properties)
        {
            var value = new List<List<PropertySymbolInfo>>();
            GetPropertiesRecursive(properties, value);
            return value;
        }

        private void GetPropertiesRecursive(PropertyDependence dependences, List<List<PropertySymbolInfo>> value)
        {
            AddProperties(dependences.Dependences, value);
            foreach (var propertyDependences in dependences.PropertiesDependences)
                GetPropertiesRecursive(propertyDependences.Value, value);
        }

        public static void Reset()
        {
        }

        public override void Visit(SyntaxNode node)
        {
            if (node == null)
                return;
            if (node is StatementSyntax)
                _properties.ResetLast();
            base.Visit(node);
            if (!_proceed)
                foreach (var childNode in node.ChildNodes())
                {
                    var getMembersVisitor = new GetMembersVisitor(this);
                    getMembersVisitor.Visit(childNode);
                    AddProperties(getMembersVisitor._properties);
                    if (_returnProperties != getMembersVisitor._returnProperties && getMembersVisitor._returnProperties != null)
                        AddReturnProperties(getMembersVisitor._returnProperties);
                }
        }

        public override void VisitQueryExpression(QueryExpressionSyntax node)
        {
            _linqIndex++;
            var getMembersVisitor = new GetMembersVisitor(this);
            getMembersVisitor.Visit(node.FromClause);
            AddProperties(getMembersVisitor._properties);
            getMembersVisitor = new GetMembersVisitor(this);
            getMembersVisitor.Visit(node.Body);
            AddProperties(getMembersVisitor._properties);
            _properties.Last = getMembersVisitor._properties.Last;
            _proceed = true;
        }

        public override void VisitQueryContinuation(QueryContinuationSyntax node)
        {
            Visit(node.Body);
            _proceed = true;
        }

        public override void VisitJoinClause(JoinClauseSyntax node)
        {
            var membersVisitor = new GetMembersVisitor(this);
            membersVisitor.Visit(node.InExpression);
            _variables.Add(node.Identifier.ValueText, membersVisitor._properties);
            _fromVariables.Add(node.Identifier.ValueText, _linqIndex);
            AddProperties(membersVisitor._properties);
            Visit(node.LeftExpression);
            _proceed = false;
            Visit(node.RightExpression);
            if (node.Into != null)
            {
                _variables.Add(node.Into.Identifier.ValueText, membersVisitor._properties);
                _fromVariables.Add(node.Into.Identifier.ValueText, _linqIndex);
                _fromVariables.Remove(node.Identifier.ValueText);
                _variables.Remove(node.Identifier.ValueText);
            }
            _proceed = true;
        }

        public override void VisitQueryBody(QueryBodySyntax node)
        {
            foreach (var clause in node.Clauses)
                Visit(clause);
            _proceed = false;

            if (node.Continuation == null)
            {
                Visit(node.SelectOrGroup);
                _proceed = true;
                return;
            }

            Visit(node.SelectOrGroup);
            _properties.Last = _properties.Last;
            foreach (string variable in _fromVariables.Where(v => v.Value == _linqIndex).Select(v => v.Key).ToList())
            {
                AddProperties(GetProperties(_variables[variable]));
                _fromVariables.Remove(variable);
                _variables.Remove(variable);
            }
            var variableDependences = new PropertyDependence();
            AddProperties(_properties.Last, variableDependences);
            _variables.Add(node.Continuation.Identifier.ValueText, variableDependences);
            _fromVariables.Add(node.Continuation.Identifier.ValueText, _linqIndex);
            Visit(node.Continuation);
            _proceed = true;
        }

        public override void VisitFromClause(FromClauseSyntax node)
        {
            var membersVisitor = new GetMembersVisitor(this);
            membersVisitor.Visit(node.Expression);
            _variables.Add(node.Identifier.ValueText, membersVisitor._properties);
            _fromVariables.Add(node.Identifier.ValueText, _linqIndex);
            AddProperties(membersVisitor._properties);
            _proceed = true;
        }

        public override void VisitLetClause(LetClauseSyntax node)
        {
            var membersVisitor = new GetMembersVisitor(this);
            membersVisitor.Visit(node.Expression);
            _variables.Add(node.Identifier.ValueText, membersVisitor._properties);
            AddProperties(membersVisitor._properties);
            _fromVariables.Add(node.Identifier.ValueText, _linqIndex);
            _proceed = true;
        }

        public override void VisitWhereClause(WhereClauseSyntax node)
        {
            var membersVisitor = new GetMembersVisitor(this);
            membersVisitor.Visit(node.Condition);
            AddProperties(membersVisitor._properties);
            _proceed = true;
        }

        public override void VisitSelectClause(SelectClauseSyntax node)
        {
            Visit(node.Expression);
            foreach (string variable in _fromVariables.Where(v => v.Value == _linqIndex).Select(v => v.Key).ToList())
            {
                AddProperties(GetProperties(_variables[variable]));
                _fromVariables.Remove(variable);
                _variables.Remove(variable);
            }
            _linqIndex--;
            _proceed = true;
        }

        public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            if (node.Initializer != null)
            {
                _properties.Last = _properties.Last;
                foreach (var m in node.Initializer.Expressions)
                {
                    var binary = m as BinaryExpressionSyntax;
                    if (binary != null && binary.Kind == SyntaxKind.AssignExpression)
                    {
                        var initializerVisitor = new GetMembersVisitor(this);
                        initializerVisitor.Visit(binary.Right);
                        var propertyName = ((IdentifierNameSyntax)binary.Left).Identifier.ValueText;
                        if (_properties.PropertiesDependences.ContainsKey(propertyName))
                            _properties.PropertiesDependences[propertyName] = initializerVisitor._properties;
                        else
                            _properties.PropertiesDependences.Add(propertyName, initializerVisitor._properties);
                    }
                    else
                        Visit(m);
                }
                _properties.Last = new PropertyDependence { PropertiesDependences = _properties.PropertiesDependences };
                _proceed = true;
            }
        }

        public override void VisitAnonymousObjectCreationExpression(AnonymousObjectCreationExpressionSyntax node)
        {
            _properties.Last = _properties.Last;
            foreach (var m in node.Initializers)
            {
                var initializerVisitor = new GetMembersVisitor(this);
                initializerVisitor.Visit(m);
                AddProperties(initializerVisitor._properties);
            }
            _properties.Last = new PropertyDependence { PropertiesDependences = _properties.PropertiesDependences };
            _proceed = true;
        }

        public override void VisitAnonymousObjectMemberDeclarator(AnonymousObjectMemberDeclaratorSyntax node)
        {
            var expressionVisitor = new GetMembersVisitor(this);
            expressionVisitor.Visit(node.Expression);
            _properties.PropertiesDependences.Add(node.NameEquals == null ? ((MemberAccessExpressionSyntax)node.Expression).Name.Identifier.ValueText : node.NameEquals.Name.Identifier.ValueText, expressionVisitor._properties);
        }

        public override void VisitGroupClause(GroupClauseSyntax node)
        {
            var groupExpressionVisitor = new GetMembersVisitor(this);
            groupExpressionVisitor.Visit(node.GroupExpression);
            var byExpressionVisitor = new GetMembersVisitor(this);
            byExpressionVisitor.Visit(node.ByExpression);
            AddProperties(groupExpressionVisitor._properties);
            _properties.PropertiesDependences.Add("Key", byExpressionVisitor._properties);
            _proceed = true;
        }

        public override void VisitBlock(BlockSyntax node)
        {
            var membersVisitor = new GetMembersVisitor(this, new Dictionary<string, PropertyDependence>(_variables));
            foreach (var statement in node.Statements)
            {
                membersVisitor.Visit(statement);
                membersVisitor._proceed = false;
            }
            AddProperties(membersVisitor._properties);
            AddReturnProperties(membersVisitor._returnProperties);
            _proceed = true;
        }

        public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var expressionSymbol = _semanticModel.GetSymbolInfo(node.Expression).Symbol;
            if (expressionSymbol is ITypeSymbol)
                return;
            var parameterSymbol = expressionSymbol as IParameterSymbol;
            PropertyDependence variableProperties;
            var propertySymbol = _semanticModel.GetSymbolInfo(node).Symbol as PropertySymbol;
            if (propertySymbol == null)
                return;
            bool systemProperty = propertySymbol.ContainingType.ContainingNamespace != null && propertySymbol.ContainingType.ContainingNamespace.ToString().StartsWith("System");

            Action applyOnParameter = () =>
            {
                bool knownVariable = _variables.TryGetValue(parameterSymbol.Name, out variableProperties);
                if (knownVariable && variableProperties != null)
                {
                    PropertyDependence newLast;
                    if (variableProperties.PropertiesDependences.TryGetValue(propertySymbol.Name, out newLast))
                    {
                        _properties.Last = newLast.Last;
                        _properties = newLast;
                        _proceed = true;
                        return;
                    }
                }
                if (systemProperty)
                    return;
                if (knownVariable)
                {
                    if (variableProperties == null || variableProperties.Last.Dependences.Count == 0)
                        AddProperty(new List<PropertySymbolInfo>() { propertySymbol });
                    else
                    {
                        foreach (var dp in variableProperties.Last.Dependences)
                            AddProperty(new List<PropertySymbolInfo>(dp) { propertySymbol });
                    }
                    _proceed = true;
                }
            };
            if (parameterSymbol != null)
            {
                applyOnParameter();
                if (_proceed)
                    return;
            }

            {
                var membersVisitor = new GetMembersVisitor(this);
                membersVisitor.Visit(node.Expression);
                if ((parameterSymbol = membersVisitor._properties.ParameterSymbol) != null)
                {
                    applyOnParameter();
                    if (_proceed)
                        return;
                }
                AddProperties(membersVisitor._properties, _properties);
                PropertyDependence newLast;
                if (membersVisitor._properties.Last.PropertiesDependences.TryGetValue(propertySymbol.Name, out newLast))
                {
                    AddProperties(newLast);
                    _properties.Last = newLast.Last;
                    _proceed = true;
                    return;
                }
                if (systemProperty)
                    return;
                if (membersVisitor._properties.Dependences.Count != 0 || membersVisitor._properties.PropertiesDependences.Count != 0)
                {
                    var dependencesList = new List<List<PropertySymbolInfo>>();
                    foreach (var pd in membersVisitor._properties.Last.Dependences)
                    {
                        var dependences = new List<PropertySymbolInfo>(pd);
                        dependences.Add(propertySymbol);
                        AddProperty(dependences);
                        dependencesList.Add(dependences);
                    }
                    _properties.Last = new PropertyDependence { Dependences = dependencesList };
                    _proceed = true;
                }
            }
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var methodSymbol = (MethodSymbol)_semanticModel.GetSymbolInfo(node).Symbol;
            if (!(methodSymbol.IsStatic || SpecificationEquivalentMethod.GetSpecificationEquivalentMethod(_specificationsElements, ref methodSymbol, _semanticModelPerMethods, _extensionMethods) || methodSymbol.ContainingNamespace.ToString() == "System.Linq"))
                return;
            var argumentExpressions = node.ArgumentList.Arguments.Select(a => a.Expression).ToList();
            var memberAccessExpression = node.Expression as MemberAccessExpressionSyntax;
            if (memberAccessExpression != null &&
                (_semanticModel.GetSymbolInfo(memberAccessExpression.Expression).Symbol as ITypeSymbol) == null)
                argumentExpressions.Insert(0, memberAccessExpression.Expression);
            string methodSymbolString = methodSymbol.ToString();
            MethodDeclarationSyntax method;
            if (_methodPerMethodSymbols.TryGetValue(methodSymbolString, out method))
            {
                if (_alreadyCalledMethods.Contains(methodSymbol))
                {
                    if (++_failed > MaxFailed)
                        return;
                }
                else
                    _alreadyCalledMethods.Add(methodSymbol);
                if (_getMethods.Values.SelectMany(m => m).Contains(method))
                {
                    var membersVisitor = new GetMembersVisitor(this);
                    membersVisitor.Visit(argumentExpressions[0]);
                    AddProperties(membersVisitor._properties);
                    AddProperty(new List<PropertySymbolInfo>(LastOrDefault(membersVisitor._properties.Dependences))
                    {
                        new PropertySymbolInfo(methodSymbol.ReturnType,
                            SpecificationMethods.GetPropertyNameFromMethod(method), methodSymbol.Parameters[0].Type,
                            method)
                    });
                }
                else
                {
                    var variables = new Dictionary<string, PropertyDependence>();
                    int index = 0;
                    foreach (var argumentExpression in argumentExpressions)
                    {
                        var membersVisitor = new GetMembersVisitor(this);
                        membersVisitor.Visit(argumentExpression);
                        AddProperties(membersVisitor._properties);
                        if (membersVisitor._properties.Dependences.GroupBy(p => p.Count).All(g => g.Count() == 1))
                            variables.Add(methodSymbol.Parameters[index].Name, membersVisitor._properties);
                        index++;
                    }
                    {
                        var semanticModel = _semanticModelPerMethods[method];
                        var membersVisitor = new GetMembersVisitor(semanticModel, _specificationsElements,
                            _serverFxDALInterfacesNamespace, _semanticModelPerMethods, _methodPerMethodSymbols,
                            _getMethods, _extensionMethods, variables, _fromVariables, _linqIndex, _alreadyCalledMethods,
                            _definePropertyDependences, _failed);
                        membersVisitor._returnProperties = new PropertyDependence();
                        membersVisitor.Visit(method.Body);
                        AddProperties(membersVisitor._properties);
                        if (! (membersVisitor._returnProperties.Dependences.Count == 0 && membersVisitor._returnProperties.PropertiesDependences.Count == 0))
                            _properties.Last = membersVisitor._returnProperties;
                        _proceed = true;
                    }
                }
            }
            else
            {
                if (methodSymbol.ContainingNamespace.ToString() == "System.Linq")
                {
                    var membersVisitor = new GetMembersVisitor(this);
                    membersVisitor.Visit(argumentExpressions[0]);
                    if (membersVisitor._properties.Last != null)
                        _properties.Last = membersVisitor._properties.Last;
                    AddProperties(membersVisitor._properties);
                    Action<int, Action<PropertyDependence>> visitLambda = (argumentIndex, addProperties) =>
                    {
                        var lambdaExpression = argumentExpressions[argumentIndex] as SimpleLambdaExpressionSyntax;
                        if (lambdaExpression != null)
                        {
                            var variables = new Dictionary<string, PropertyDependence>(_variables);
                            if (!variables.ContainsKey(lambdaExpression.Parameter.Identifier.ValueText))
                                variables.Add(lambdaExpression.Parameter.Identifier.ValueText, _properties);
                            else
                                variables[lambdaExpression.Parameter.Identifier.ValueText] = _properties;
                            membersVisitor = new GetMembersVisitor(this, variables);
                            membersVisitor._returnProperties = null;
                            membersVisitor.Visit(lambdaExpression.Body);
                            addProperties(membersVisitor._properties);
                            _proceed = true;
                        }
                    };
                    switch (argumentExpressions.Count)
                    {
                        case 1:
                            _properties.Last = membersVisitor._properties.Last;
                            _proceed = true;
                            break;
                        case 2:
                            bool applyLast = false;
                            switch (methodSymbol.Name)
                            {
                                case "Where":
                                case "OrderBy":
                                case "OrderByDescending":
                                case "ThenBy":
                                case "ThenByDescending":
                                case "Take":
                                case "Skip":
                                case "First":
                                case "FirstOrDefault":
                                case "Last":
                                case "LastOrDefault":
                                case "Single":
                                case "SingleOrDefault":
                                    _properties.Last = membersVisitor._properties.Last;
                                    break;
                                case "GroupBy":
                                    _properties.Last = membersVisitor._properties.Last;
                                    visitLambda(1, pd =>
                                    {
                                        if (_properties.PropertiesDependences.ContainsKey("Key"))
                                            _properties.PropertiesDependences["Key"] = pd;
                                        else
                                            _properties.PropertiesDependences.Add("Key", pd);
                                    });
                                    _properties.ResetLast();
                                    return;
                                case "Union":
                                case "Intersect":
                                    membersVisitor = new GetMembersVisitor(this);
                                    membersVisitor.Visit(argumentExpressions[1]);
                                    AddProperties(membersVisitor._properties);
                                    AddProperties(membersVisitor._properties.Last, _properties.Last);
                                    _proceed = true;
                                    return;
                                default:
                                    applyLast = true;
                                    break;
                            }
                            visitLambda(1, pd =>
                            {
                                AddProperties(pd);
                                if (applyLast)
                                    _properties.Last = pd.Last;
                            });
                            break;
                        case 3:
                            switch (methodSymbol.Name)
                            {
                                case "SelectMany":
                                    var last = membersVisitor._properties.Last;
                                    _properties.Last = last;
                                    var collectionDependence = new PropertyDependence();
                                    visitLambda(1, pd =>
                                    {
                                        AddProperties(pd);
                                        AddProperties(pd, collectionDependence);
                                    });
                                    _properties.ResetLast();
                                    var lambdaExpression = (ParenthesizedLambdaExpressionSyntax)argumentExpressions[2];
                                    var variables = new Dictionary<string, PropertyDependence>(_variables);
                                    var parameter = lambdaExpression.ParameterList.Parameters[0];
                                    if (!variables.ContainsKey(parameter.Identifier.ValueText))
                                        variables.Add(parameter.Identifier.ValueText, last);
                                    else
                                        variables[parameter.Identifier.ValueText] = last;
                                    parameter = lambdaExpression.ParameterList.Parameters[1];
                                    if (!variables.ContainsKey(parameter.Identifier.ValueText))
                                        variables.Add(parameter.Identifier.ValueText, collectionDependence.Last);
                                    else
                                        variables[parameter.Identifier.ValueText] = collectionDependence.Last;
                                    membersVisitor = new GetMembersVisitor(this, variables);
                                    membersVisitor.Visit(lambdaExpression.Body);
                                    AddProperties(membersVisitor._properties);
                                    _properties.Last = membersVisitor._properties;
                                    _proceed = true;
                                    return;
                            }
                            break;
                        case 5:
                            switch (methodSymbol.Name)
                            {
                                case "Join":
                                case "GroupJoin":
                                    var last = membersVisitor._properties.Last;
                                    membersVisitor = new GetMembersVisitor(this);
                                    membersVisitor.Visit(argumentExpressions[1]);
                                    AddProperties(membersVisitor._properties);
                                    var joinLast = membersVisitor._properties.Last;
                                    _properties.Last = last;
                                    visitLambda(2, pd => AddProperties(pd));
                                    _proceed = false;
                                    _properties.Last = joinLast;
                                    visitLambda(3, pd => AddProperties(pd));
                                    _proceed = false;
                                    _properties.ResetLast();
                                    var lambdaExpression = (ParenthesizedLambdaExpressionSyntax)argumentExpressions[4];
                                    var variables = new Dictionary<string, PropertyDependence>(_variables);
                                    var parameter = lambdaExpression.ParameterList.Parameters[0];
                                    if (!variables.ContainsKey(parameter.Identifier.ValueText))
                                        variables.Add(parameter.Identifier.ValueText, last);
                                    else
                                        variables[parameter.Identifier.ValueText] = last;
                                    parameter = lambdaExpression.ParameterList.Parameters[1];
                                    if (!variables.ContainsKey(parameter.Identifier.ValueText))
                                        variables.Add(parameter.Identifier.ValueText, joinLast);
                                    else
                                        variables[parameter.Identifier.ValueText] = joinLast;
                                    membersVisitor = new GetMembersVisitor(this, variables);
                                    membersVisitor.Visit(lambdaExpression.Body);
                                    AddProperties(membersVisitor._properties);
                                    _properties.Last = membersVisitor._properties;
                                    _proceed = true;
                                    return;
                            }
                            break;
                    }
                }
            }
        }

        public override void VisitIdentifierName(IdentifierNameSyntax node)
        {
            PropertyDependence identifierProperties;
            if (_variables.TryGetValue(node.Identifier.ValueText, out identifierProperties))
            {
                if (identifierProperties != null)
                {
                    AddProperties(identifierProperties);
                    _properties.ParameterSymbol = identifierProperties.ParameterSymbol;
                }
                _proceed = true;
            }
        }

        public override void VisitForEachStatement(ForEachStatementSyntax node)
        {
            var getInitializerMembersVisitor = new GetMembersVisitor(this);
            getInitializerMembersVisitor.Visit(node.Expression);
            var variables = new Dictionary<string, PropertyDependence>(_variables);
            variables.Add(node.Identifier.ValueText, getInitializerMembersVisitor._properties.Last);
            AddProperties(getInitializerMembersVisitor._properties);
            var statementVisitor = new GetMembersVisitor(this, variables);
            statementVisitor.Visit(node.Statement);
            AddProperties(statementVisitor._properties);
            AddReturnProperties(statementVisitor._returnProperties);
            _proceed = true;
        }

        public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
        {
            foreach (var variable in node.Declaration.Variables)
                Visit(variable);
            _proceed = true;
        }

        public override void VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            PropertyDependence initializerProperties;
            if (node.Initializer == null)
                initializerProperties = new PropertyDependence();
            else
            {
                var parameter = _semanticModel.GetSymbolInfo(node.Initializer).Symbol as ParameterSymbol;
                if (parameter == null)
                {
                    var getInitializerMembersVisitor = new GetMembersVisitor(this);
                    getInitializerMembersVisitor.Visit(node.Initializer);
                    initializerProperties = getInitializerMembersVisitor._properties;
                    AddProperties(getInitializerMembersVisitor._properties);
                }
                else
                    initializerProperties = new PropertyDependence {ParameterSymbol = parameter};
            }
            _variables.Add(node.Identifier.ValueText, initializerProperties);
            _proceed = true;
        }

        public override void VisitEqualsValueClause(EqualsValueClauseSyntax node)
        {
            Visit(node.Value);
            _proceed = true;
        }

        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            var variable = node.Left as IdentifierNameSyntax;
            if (variable != null)
            {
                var variableName = variable.Identifier.ValueText;
                switch (node.Kind)
                {
                    case SyntaxKind.AssignExpression:
                        PropertyDependence rightProperties;
                        if (_variables.TryGetValue(variableName, out rightProperties))
                        {
                            rightProperties.Dependences.Clear();
                            rightProperties.PropertiesDependences.Clear();
                        }
                        goto case SyntaxKind.AddAssignExpression;
                    case SyntaxKind.AddAssignExpression:
                    case SyntaxKind.SubtractAssignExpression:
                    case SyntaxKind.MultiplyAssignExpression:
                    case SyntaxKind.DivideAssignExpression:
                    case SyntaxKind.ModuloAssignExpression:
                        var getRightMembersVisitor = new GetMembersVisitor(this);
                        getRightMembersVisitor.Visit(node.Right);
                        AddProperties(getRightMembersVisitor._properties);
                        if (_variables.TryGetValue(variableName, out rightProperties))
                            AddProperties(getRightMembersVisitor._properties, rightProperties);
                        _proceed = true;
                        break;
                    case SyntaxKind.AsExpression:
                        var parameter = _semanticModel.GetSymbolInfo(variable).Symbol as ParameterSymbol;
                        if (parameter != null)
                        {
                            _properties.ParameterSymbol = parameter;
                            _proceed = true;
                        }
                        break;
                }
            }
            if (!_proceed)
                switch (node.Kind)
                {
                    case SyntaxKind.AsExpression:
                        Visit(node.Left);
                        _proceed = true;
                        break;
                }
        }

        public override void VisitReturnStatement(ReturnStatementSyntax node)
        {
            var getReturnMembers = new GetMembersVisitor(this);
            getReturnMembers.Visit(node.Expression);
            AddProperties(getReturnMembers._properties);
            AddReturnProperties(getReturnMembers._properties);

            _proceed = true;
        }

        public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
        {
            var getConditionMembers = new GetMembersVisitor(this);
            getConditionMembers.Visit(node.Condition);
            AddProperties(getConditionMembers._properties);

            var getWhenTrueMembers = new GetMembersVisitor(this);
            getWhenTrueMembers.Visit(node.WhenTrue);
            AddProperties(getWhenTrueMembers._properties);

            var getWhenFalseMembers = new GetMembersVisitor(this);
            getWhenFalseMembers.Visit(node.WhenFalse);
            AddProperties(getWhenFalseMembers._properties);

            var last = new PropertyDependence();
            AddProperties(getWhenFalseMembers._properties.Last, last);
            AddProperties(getWhenTrueMembers._properties.Last, last);
            _properties.Last = last;

            _proceed = true;
        }

        private List<PropertySymbolInfo> LastOrDefault(List<List<PropertySymbolInfo>> properties)
        {
            return properties == null
                ? new List<PropertySymbolInfo>()
                : (properties.LastOrDefault() ?? new List<PropertySymbolInfo>());
        }

        private string GetPropertyName(List<PropertySymbolInfo> property)
        {
            var sb = new StringBuilder();
            foreach (var p in property)
            {
                sb.Append(p.Name);
                sb.Append(".");
            }
            return sb.ToString();
        }

        private void AddProperty(List<PropertySymbolInfo> property, List<List<PropertySymbolInfo>> properties = null)
        {
            if (properties == null)
                properties = _properties.Dependences;
            if (property == null)
                return;
            var propertyName = GetPropertyName(property);
            if (!properties.Any(p => GetPropertyName(p) == propertyName))
            {
                property = property.Select(p =>
                {
                    List<string> classes;
                    TypeSymbol typeSymbol;
                    if (
                        _specificationsElements.ClassesPerInterfaces.TryGetValue(p.ContainingType.FullName, out classes) &&
                        (typeSymbol =
                            _specificationsElements.TypeSymbols.FirstOrDefault(ts => ts.Name == classes.Single())) !=
                        null)
                    {
                        var propertySymbol = (PropertySymbol)typeSymbol.GetMembers(p.Name).FirstOrDefault();
                        if (propertySymbol != null)
                            return new PropertySymbolInfo(propertySymbol);
                    }
                    return p;
                }).ToList();
                properties.Add(property);
            }
        }

        private void AddProperties(IEnumerable<List<PropertySymbolInfo>> addedProperties, List<List<PropertySymbolInfo>> properties = null)
        {
            foreach (var property in addedProperties)
            {
                AddProperty(property, properties);
                foreach (var p in property)
                    p.FromOriginalMethod = _fromOriginalMethod;
            }
        }

        private void AddProperties(PropertyDependence addedProperties, PropertyDependence properties = null)
        {
            if (properties == null)
                properties = _properties;
            AddProperties(addedProperties.Dependences, properties.Dependences);
            foreach (var addedPropertyDependences in addedProperties.PropertiesDependences)
            {
                PropertyDependence propertyDependences, last = null;
                if (!properties.PropertiesDependences.TryGetValue(addedPropertyDependences.Key, out propertyDependences))
                    properties.PropertiesDependences.Add(addedPropertyDependences.Key, propertyDependences = new PropertyDependence());
                else
                {
                    last = propertyDependences.Last;
                    propertyDependences.ResetLast();
                }
                AddProperties(addedPropertyDependences.Value, propertyDependences);
                if (last != null)
                {
                    AddProperties(propertyDependences.Last, last);
                    propertyDependences.Last = last;
                }
            }
        }

        private void AddReturnProperties(PropertyDependence addedProperties)
        {
            if (addedProperties == null)
                return;
            if (_returnProperties == null)
                _returnProperties = new PropertyDependence();
            AddProperties(addedProperties.Last, _returnProperties);
        }
    }

    class PropertyDependence
    {
        private PropertyDependence _last;
        public PropertyDependence Last
        {
            get
            {
                if (_last == null)
                {
                    _last = new PropertyDependence { Dependences = new List<List<PropertySymbolInfo>>(), PropertiesDependences = new Dictionary<string, PropertyDependence>(PropertiesDependences) };
                    if (Dependences.Count != 0)
                        _last.Dependences.Add(Dependences.Last());
                }
                return _last;
            }
            set { _last = value; }
        }

        public void ResetLast()
        {
            _last = null;
        }

        public ParameterSymbol ParameterSymbol { get; set; }

        private List<List<PropertySymbolInfo>> _dependences;
        public List<List<PropertySymbolInfo>> Dependences
        {
            get { return _dependences ?? (_dependences = new List<List<PropertySymbolInfo>>()); }
            set { _dependences = value; }
        }

        private Dictionary<string, PropertyDependence> _propertiesDependences;
        public Dictionary<string, PropertyDependence> PropertiesDependences
        {
            get { return _propertiesDependences ?? (_propertiesDependences = new Dictionary<string, PropertyDependence>()); }
            set { _propertiesDependences = value; }
        }

        public bool Error { get; set; }
    }
}