//   OData .NET Libraries
//   Copyright (c) Microsoft Corporation
//   All rights reserved. 

//   Licensed under the Apache License, Version 2.0 (the ""License""); you may not use this file except in compliance with the License. You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0 

//   THIS CODE IS PROVIDED ON AN  *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY IMPLIED WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A PARTICULAR PURPOSE, MERCHANTABLITY OR NON-INFRINGEMENT. 

//   See the Apache Version 2.0 License for specific language governing permissions and limitations under the License.

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.OData.Edm.Annotations;
using Microsoft.OData.Edm.Csdl.Parsing.Ast;
using Microsoft.OData.Edm.Expressions;
using Microsoft.OData.Edm.Library;
using Microsoft.OData.Edm.Library.Annotations;
using Microsoft.OData.Edm.Validation;

namespace Microsoft.OData.Edm.Csdl.CsdlSemantics
{
    /// <summary>
    /// Provides semantics for CsdlMetadataModel.
    /// </summary>
    internal class CsdlSemanticsModel : EdmModelBase, IEdmCheckable
    {
        private readonly CsdlModel astModel;
        private readonly List<CsdlSemanticsSchema> schemata = new List<CsdlSemanticsSchema>();
        private readonly Dictionary<string, List<CsdlSemanticsAnnotations>> outOfLineAnnotations = new Dictionary<string, List<CsdlSemanticsAnnotations>>();
        private readonly Dictionary<CsdlAnnotation, CsdlSemanticsVocabularyAnnotation> wrappedAnnotations = new Dictionary<CsdlAnnotation, CsdlSemanticsVocabularyAnnotation>();
        private readonly Dictionary<string, List<IEdmStructuredType>> derivedTypeMappings = new Dictionary<string, List<IEdmStructuredType>>();

        public CsdlSemanticsModel(CsdlModel astModel, EdmDirectValueAnnotationsManager annotationsManager, IEnumerable<IEdmModel> referencedModels)
            : base(referencedModels, annotationsManager)
        {
            this.astModel = astModel;

            foreach (CsdlSchema schema in this.astModel.Schemata)
            {
                this.AddSchema(schema);
            }
        }

        public override IEnumerable<IEdmSchemaElement> SchemaElements
        {
            get
            {
                foreach (CsdlSemanticsSchema schema in this.schemata)
                {
                    foreach (IEdmSchemaType type in schema.Types)
                    {
                        yield return type;
                    }

                    foreach (IEdmSchemaElement function in schema.Operations)
                    {
                        yield return function;
                    }

                    foreach (IEdmSchemaElement valueTerm in schema.ValueTerms)
                    {
                        yield return valueTerm;
                    }

                    foreach (IEdmEntityContainer entityContainer in schema.EntityContainers)
                    {
                        yield return entityContainer;
                    }
                }
            }
        }

        public override IEnumerable<string> DeclaredNamespaces
        {
            get { return this.GetUsedNamespaces(); }
        }

        public override IEnumerable<IEdmVocabularyAnnotation> VocabularyAnnotations
        {
            get
            {
                List<IEdmVocabularyAnnotation> result = new List<IEdmVocabularyAnnotation>();

                foreach (CsdlSemanticsSchema schema in this.schemata)
                {
                    foreach (CsdlAnnotations sourceAnnotations in ((CsdlSchema)schema.Element).OutOfLineAnnotations)
                    {
                        CsdlSemanticsAnnotations annotations = new CsdlSemanticsAnnotations(schema, sourceAnnotations);
                        foreach (CsdlAnnotation sourceAnnotation in sourceAnnotations.Annotations)
                        {
                            IEdmVocabularyAnnotation vocabAnnotation = this.WrapVocabularyAnnotation(sourceAnnotation, schema, null, annotations, sourceAnnotations.Qualifier);
                            vocabAnnotation.SetSerializationLocation(this, EdmVocabularyAnnotationSerializationLocation.OutOfLine);
                            vocabAnnotation.SetSchemaNamespace(this, schema.Namespace);
                            result.Add(vocabAnnotation);
                        }
                    }
                }

                foreach (IEdmSchemaElement element in this.SchemaElements)
                {
                    result.AddRange(((CsdlSemanticsElement)element).InlineVocabularyAnnotations);

                    CsdlSemanticsStructuredTypeDefinition type = element as CsdlSemanticsStructuredTypeDefinition;
                    if (type != null)
                    {
                        foreach (IEdmProperty property in type.DeclaredProperties)
                        {
                            result.AddRange(((CsdlSemanticsElement)property).InlineVocabularyAnnotations);
                        }
                    }

                    CsdlSemanticsOperation operation = element as CsdlSemanticsOperation;
                    if (operation != null)
                    {
                        foreach (IEdmOperationParameter parameter in operation.Parameters)
                        {
                            result.AddRange(((CsdlSemanticsElement)parameter).InlineVocabularyAnnotations);
                        }
                    }

                    CsdlSemanticsEntityContainer container = element as CsdlSemanticsEntityContainer;
                    if (container != null)
                    {
                        foreach (IEdmEntityContainerElement containerElement in container.Elements)
                        {
                            result.AddRange(((CsdlSemanticsElement)containerElement).InlineVocabularyAnnotations);
                        }
                    }
                }

                return result;
            }
        }

        /// <summary>
        /// Gets an error if one exists with the current object.
        /// </summary>
        public IEnumerable<EdmError> Errors
        {
            get
            {
                List<EdmError> errors = new List<EdmError>();

                HashSetInternal<string> usedAlias = new HashSetInternal<string>();
                var usedNamespaces = this.GetUsedNamespaces();
                var mappings = this.GetNamespaceAliases();

                if (usedNamespaces != null && mappings != null)
                {
                    foreach (var ns in usedNamespaces)
                    {
                        string alias;
                        if (mappings.TryGetValue(ns, out alias) && !usedAlias.Add(alias))
                        {
                            errors.Add(new EdmError(this.Location(), EdmErrorCode.DuplicateAlias, Strings.CsdlSemantics_DuplicateAlias(ns, alias)));
                        }
                    }
                }

                foreach (CsdlSemanticsSchema schema in this.schemata)
                {
                    errors.AddRange(schema.Errors());
                }

                return errors;
            }
        }

        /// <summary>
        /// Searches for vocabulary annotations specified by this model.
        /// </summary>
        /// <param name="element">The annotated element.</param>
        /// <returns>The vocabulary annotations for the element.</returns>
        public override IEnumerable<IEdmVocabularyAnnotation> FindDeclaredVocabularyAnnotations(IEdmVocabularyAnnotatable element)
        {
            // Include the inline annotations only if this model is the one that defined them.
            CsdlSemanticsElement semanticsElement = element as CsdlSemanticsElement;
            IEnumerable<IEdmVocabularyAnnotation> inlineAnnotations = semanticsElement != null && semanticsElement.Model == this ? semanticsElement.InlineVocabularyAnnotations : Enumerable.Empty<IEdmVocabularyAnnotation>();

            List<CsdlSemanticsAnnotations> elementAnnotations;
            string fullName = EdmUtil.FullyQualifiedName(element);

            if (fullName != null && this.outOfLineAnnotations.TryGetValue(fullName, out elementAnnotations))
            {
                List<IEdmVocabularyAnnotation> result = new List<IEdmVocabularyAnnotation>();

                foreach (CsdlSemanticsAnnotations annotations in elementAnnotations)
                {
                    foreach (CsdlAnnotation annotation in annotations.Annotations.Annotations)
                    {
                        result.Add(this.WrapVocabularyAnnotation(annotation, annotations.Context, null, annotations, annotations.Annotations.Qualifier));
                    }
                }

                return inlineAnnotations.Concat(result);
            }

            return inlineAnnotations;
        }

        public override IEnumerable<IEdmStructuredType> FindDirectlyDerivedTypes(IEdmStructuredType baseType)
        {
            List<IEdmStructuredType> candidates;
            if (this.derivedTypeMappings.TryGetValue(((IEdmSchemaType)baseType).Name, out candidates))
            {
                return candidates.Where(t => t.BaseType == baseType);
            }

            return Enumerable.Empty<IEdmStructuredType>();
        }

        public string ReplaceAlias(string name)
        {
            var mappings = this.GetNamespaceAliases();
            var list = this.GetUsedNamespaces();

            if (list != null && mappings != null && name.Contains("."))
            {
                var typeAlias = name.Split('.').First();
                var ns = list.FirstOrDefault(n => mappings.Get(n) == typeAlias);
                return (ns != null) ? string.Format(CultureInfo.InvariantCulture, "{0}.{1}", ns, name.Substring(typeAlias.Length + 1)) : null;
            }

            return null;
        }

        internal static IEdmExpression WrapExpression(CsdlExpressionBase expression, IEdmEntityType bindingContext, CsdlSemanticsSchema schema)
        {
            if (expression != null)
            {
                switch (expression.ExpressionKind)
                {
                    case EdmExpressionKind.Cast:
                        return new CsdlSemanticsCastExpression((CsdlCastExpression)expression, bindingContext, schema);
                    case EdmExpressionKind.BinaryConstant:
                        return new CsdlSemanticsBinaryConstantExpression((CsdlConstantExpression)expression, schema);
                    case EdmExpressionKind.BooleanConstant:
                        return new CsdlSemanticsBooleanConstantExpression((CsdlConstantExpression)expression, schema);
                    case EdmExpressionKind.Collection:
                        return new CsdlSemanticsCollectionExpression((CsdlCollectionExpression)expression, bindingContext, schema);
                    case EdmExpressionKind.DateTimeOffsetConstant:
                        return new CsdlSemanticsDateTimeOffsetConstantExpression((CsdlConstantExpression)expression, schema);
                    case EdmExpressionKind.DecimalConstant:
                        return new CsdlSemanticsDecimalConstantExpression((CsdlConstantExpression)expression, schema);
                    case EdmExpressionKind.EntitySetReference:
                        return new CsdlSemanticsEntitySetReferenceExpression((CsdlEntitySetReferenceExpression)expression, bindingContext, schema);
                    case EdmExpressionKind.EnumMemberReference:
                        return new CsdlSemanticsEnumMemberReferenceExpression((CsdlEnumMemberReferenceExpression)expression, bindingContext, schema);
                    case EdmExpressionKind.FloatingConstant:
                        return new CsdlSemanticsFloatingConstantExpression((CsdlConstantExpression)expression, schema);
                    case EdmExpressionKind.Null:
                        return new CsdlSemanticsNullExpression((CsdlConstantExpression)expression, schema);
                    case EdmExpressionKind.OperationApplication:
                        return new CsdlSemanticsApplyExpression((CsdlApplyExpression)expression, bindingContext, schema);
                    case EdmExpressionKind.OperationReference:
                        return new CsdlSemanticsOperationReferenceExpression((CsdlOperationReferenceExpression)expression, bindingContext, schema);
                    case EdmExpressionKind.GuidConstant:
                        return new CsdlSemanticsGuidConstantExpression((CsdlConstantExpression)expression, schema);
                    case EdmExpressionKind.If:
                        return new CsdlSemanticsIfExpression((CsdlIfExpression)expression, bindingContext, schema);
                    case EdmExpressionKind.IntegerConstant:
                        return new CsdlSemanticsIntConstantExpression((CsdlConstantExpression)expression, schema);
                    case EdmExpressionKind.IsType:
                        return new CsdlSemanticsIsTypeExpression((CsdlIsTypeExpression)expression, bindingContext, schema);
                    case EdmExpressionKind.LabeledExpressionReference:
                        return new CsdlSemanticsLabeledExpressionReferenceExpression((CsdlLabeledExpressionReferenceExpression)expression, bindingContext, schema);
                    case EdmExpressionKind.Labeled:
                        return schema.WrapLabeledElement((CsdlLabeledExpression)expression, bindingContext);
                    case EdmExpressionKind.ParameterReference:
                        return new CsdlSemanticsParameterReferenceExpression((CsdlParameterReferenceExpression)expression, bindingContext, schema);
                    case EdmExpressionKind.Path:
                        return new CsdlSemanticsPathExpression((CsdlPathExpression)expression, bindingContext, schema);
                    case EdmExpressionKind.PropertyPath:
                        return new CsdlSemanticsPropertyPathExpression((CsdlPropertyPathExpression)expression, bindingContext, schema);
                    case EdmExpressionKind.PropertyReference:
                        return new CsdlSemanticsPropertyReferenceExpression((CsdlPropertyReferenceExpression)expression, bindingContext, schema);
                    case EdmExpressionKind.Record:
                        return new CsdlSemanticsRecordExpression((CsdlRecordExpression)expression, bindingContext, schema);
                    case EdmExpressionKind.StringConstant:
                        return new CsdlSemanticsStringConstantExpression((CsdlConstantExpression)expression, schema);
                    case EdmExpressionKind.DurationConstant:
                        return new CsdlSemanticsDurationConstantExpression((CsdlConstantExpression)expression, schema);
                }
            }

            return null;
        }

        internal static IEdmTypeReference WrapTypeReference(CsdlSemanticsSchema schema, CsdlTypeReference type)
        {
            var typeReference = type as CsdlNamedTypeReference;
            if (typeReference != null)
            {
                var primitiveReference = typeReference as CsdlPrimitiveTypeReference;
                if (primitiveReference != null)
                {
                    switch (primitiveReference.Kind)
                    {
                        case EdmPrimitiveTypeKind.Boolean:
                        case EdmPrimitiveTypeKind.Byte:
                        case EdmPrimitiveTypeKind.Double:
                        case EdmPrimitiveTypeKind.Guid:
                        case EdmPrimitiveTypeKind.Int16:
                        case EdmPrimitiveTypeKind.Int32:
                        case EdmPrimitiveTypeKind.Int64:
                        case EdmPrimitiveTypeKind.SByte:
                        case EdmPrimitiveTypeKind.Single:
                        case EdmPrimitiveTypeKind.Stream:
                            return new CsdlSemanticsPrimitiveTypeReference(schema, primitiveReference);

                        case EdmPrimitiveTypeKind.Binary:
                            return new CsdlSemanticsBinaryTypeReference(schema, (CsdlBinaryTypeReference)primitiveReference);

                        case EdmPrimitiveTypeKind.DateTimeOffset:
                        case EdmPrimitiveTypeKind.Duration:
                            return new CsdlSemanticsTemporalTypeReference(schema, (CsdlTemporalTypeReference)primitiveReference);

                        case EdmPrimitiveTypeKind.Decimal:
                            return new CsdlSemanticsDecimalTypeReference(schema, (CsdlDecimalTypeReference)primitiveReference);

                        case EdmPrimitiveTypeKind.String:
                            return new CsdlSemanticsStringTypeReference(schema, (CsdlStringTypeReference)primitiveReference);

                        case EdmPrimitiveTypeKind.Geography:
                        case EdmPrimitiveTypeKind.GeographyPoint:
                        case EdmPrimitiveTypeKind.GeographyLineString:
                        case EdmPrimitiveTypeKind.GeographyPolygon:
                        case EdmPrimitiveTypeKind.GeographyCollection:
                        case EdmPrimitiveTypeKind.GeographyMultiPolygon:
                        case EdmPrimitiveTypeKind.GeographyMultiLineString:
                        case EdmPrimitiveTypeKind.GeographyMultiPoint:
                        case EdmPrimitiveTypeKind.Geometry:
                        case EdmPrimitiveTypeKind.GeometryPoint:
                        case EdmPrimitiveTypeKind.GeometryLineString:
                        case EdmPrimitiveTypeKind.GeometryPolygon:
                        case EdmPrimitiveTypeKind.GeometryCollection:
                        case EdmPrimitiveTypeKind.GeometryMultiPolygon:
                        case EdmPrimitiveTypeKind.GeometryMultiLineString:
                        case EdmPrimitiveTypeKind.GeometryMultiPoint:
                            return new CsdlSemanticsSpatialTypeReference(schema, (CsdlSpatialTypeReference)primitiveReference);
                    }
                }

                return new CsdlSemanticsNamedTypeReference(schema, typeReference);
            }

            var typeExpression = type as CsdlExpressionTypeReference;
            if (typeExpression != null)
            {
                var collectionType = typeExpression.TypeExpression as CsdlCollectionType;
                if (collectionType != null)
                {
                    return new CsdlSemanticsCollectionTypeExpression(typeExpression, new CsdlSemanticsCollectionTypeDefinition(schema, collectionType));
                }

                var entityReferenceType = typeExpression.TypeExpression as CsdlEntityReferenceType;
                if (entityReferenceType != null)
                {
                    return new CsdlSemanticsEntityReferenceTypeExpression(typeExpression, new CsdlSemanticsEntityReferenceTypeDefinition(schema, entityReferenceType));
                }
            }

            return null;
        }

        internal IEnumerable<IEdmVocabularyAnnotation> WrapInlineVocabularyAnnotations(CsdlSemanticsElement element, CsdlSemanticsSchema schema)
        {
            IEdmVocabularyAnnotatable vocabularyAnnotatableElement = element as IEdmVocabularyAnnotatable;
            if (vocabularyAnnotatableElement != null)
            {
                IEnumerable<CsdlAnnotation> vocabularyAnnotations = element.Element.VocabularyAnnotations;
                if (vocabularyAnnotations.FirstOrDefault() != null)
                {
                    List<IEdmVocabularyAnnotation> wrappedAnnotations = new List<IEdmVocabularyAnnotation>();
                    foreach (CsdlAnnotation vocabularyAnnotation in vocabularyAnnotations)
                    {
                        IEdmVocabularyAnnotation vocabAnnotation = this.WrapVocabularyAnnotation(vocabularyAnnotation, schema, vocabularyAnnotatableElement, null, vocabularyAnnotation.Qualifier);
                        vocabAnnotation.SetSerializationLocation(this, EdmVocabularyAnnotationSerializationLocation.Inline);
                        wrappedAnnotations.Add(vocabAnnotation);
                    }

                    return wrappedAnnotations;
                }
            }

            return Enumerable.Empty<IEdmVocabularyAnnotation>();
        }

        private IEdmVocabularyAnnotation WrapVocabularyAnnotation(CsdlAnnotation annotation, CsdlSemanticsSchema schema, IEdmVocabularyAnnotatable targetContext, CsdlSemanticsAnnotations annotationsContext, string qualifier)
        {
            CsdlSemanticsVocabularyAnnotation result;

            // Guarantee that multiple calls to wrap a given annotation all return the same object.
            if (this.wrappedAnnotations.TryGetValue(annotation, out result))
            {
                return result;
            }

            result = (CsdlSemanticsVocabularyAnnotation)new CsdlSemanticsValueAnnotation(schema, targetContext, annotationsContext, annotation, qualifier);
            
            this.wrappedAnnotations[annotation] = result;
            return result;
        }

        private void AddSchema(CsdlSchema schema)
        {
            CsdlSemanticsSchema schemaWrapper = new CsdlSemanticsSchema(this, schema);
            this.schemata.Add(schemaWrapper);

            foreach (IEdmSchemaType type in schemaWrapper.Types)
            {
                CsdlSemanticsStructuredTypeDefinition structuredType = type as CsdlSemanticsStructuredTypeDefinition;
                if (structuredType != null)
                {
                    string baseTypeNamespace;
                    string baseTypeName;
                    string baseTypeFullName = ((CsdlNamedStructuredType)structuredType.Element).BaseTypeName;
                    if (baseTypeFullName != null)
                    {
                        EdmUtil.TryGetNamespaceNameFromQualifiedName(baseTypeFullName, out baseTypeNamespace, out baseTypeName);
                        if (baseTypeName != null)
                        {
                            List<IEdmStructuredType> derivedTypes;
                            if (!this.derivedTypeMappings.TryGetValue(baseTypeName, out derivedTypes))
                            {
                                derivedTypes = new List<IEdmStructuredType>();
                                this.derivedTypeMappings[baseTypeName] = derivedTypes;
                            }

                            derivedTypes.Add(structuredType);
                        }
                    }
                }

                RegisterElement(type);
            }

            foreach (IEdmOperation function in schemaWrapper.Operations)
            {
                RegisterElement(function);
            }

            foreach (IEdmValueTerm valueTerm in schemaWrapper.ValueTerms)
            {
                RegisterElement(valueTerm);
            }

            foreach (IEdmEntityContainer container in schemaWrapper.EntityContainers)
            {
                RegisterElement(container);
            }

            if (!string.IsNullOrEmpty(schema.Alias))
            {
                this.SetNamespaceAlias(schema.Namespace, schema.Alias);
            }

            foreach (CsdlAnnotations schemaOutOfLineAnnotations in schema.OutOfLineAnnotations)
            {
                string target = schemaOutOfLineAnnotations.Target;
                string replaced = this.ReplaceAlias(target);
                if (replaced != null)
                {
                    target = replaced;
                }

                List<CsdlSemanticsAnnotations> annotations;
                if (!this.outOfLineAnnotations.TryGetValue(target, out annotations))
                {
                    annotations = new List<CsdlSemanticsAnnotations>();
                    this.outOfLineAnnotations[target] = annotations;
                }

                annotations.Add(new CsdlSemanticsAnnotations(schemaWrapper, schemaOutOfLineAnnotations));
            }

            var edmVersion = this.GetEdmVersion();
            if (edmVersion == null || edmVersion < schema.Version)
            {
                this.SetEdmVersion(schema.Version);
            }
        }
    }
}
