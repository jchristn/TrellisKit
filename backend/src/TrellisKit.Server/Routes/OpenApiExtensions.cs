namespace TrellisKit.Server.Routes
{
    using System;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Short helpers for describing routes in the OpenAPI document.
    /// </summary>
    public static class OpenApiExtensions
    {
        #region Public-Methods

        /// <summary>
        /// Start route metadata.
        /// </summary>
        /// <param name="summary">Summary.</param>
        /// <param name="tag">Tag.</param>
        /// <param name="description">Description, including who may call the route.</param>
        /// <returns>Metadata.</returns>
        public static OpenApiRouteMetadata Doc(string summary, string tag, string description)
        {
            return OpenApiRouteMetadata.Create(summary, tag).WithDescription(description);
        }

        /// <summary>
        /// Add a JSON request body.
        /// </summary>
        /// <param name="metadata">Metadata.</param>
        /// <param name="description">Body description.</param>
        /// <param name="required">Whether the body is required.</param>
        /// <returns>Metadata.</returns>
        public static OpenApiRouteMetadata Body(this OpenApiRouteMetadata metadata, string description, bool required = true)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            return metadata.WithRequestBody(OpenApiRequestBodyMetadata.Json(OpenApiSchemaMetadata.Create("object"), description, required));
        }

        /// <summary>
        /// Add a response.
        /// </summary>
        /// <param name="metadata">Metadata.</param>
        /// <param name="statusCode">Status code.</param>
        /// <param name="description">Description.</param>
        /// <returns>Metadata.</returns>
        public static OpenApiRouteMetadata Resp(this OpenApiRouteMetadata metadata, int statusCode, string description)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            return metadata.WithResponse(statusCode, OpenApiResponseMetadata.Create(description));
        }

        /// <summary>
        /// Add a path parameter.
        /// </summary>
        /// <param name="metadata">Metadata.</param>
        /// <param name="name">Name.</param>
        /// <param name="description">Description.</param>
        /// <returns>Metadata.</returns>
        public static OpenApiRouteMetadata PathParam(this OpenApiRouteMetadata metadata, string name, string description)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            return metadata.WithParameter(OpenApiParameterMetadata.Path(name, description, OpenApiSchemaMetadata.String()));
        }

        /// <summary>
        /// Add a query parameter.
        /// </summary>
        /// <param name="metadata">Metadata.</param>
        /// <param name="name">Name.</param>
        /// <param name="description">Description.</param>
        /// <returns>Metadata.</returns>
        public static OpenApiRouteMetadata QueryParam(this OpenApiRouteMetadata metadata, string name, string description)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            return metadata.WithParameter(OpenApiParameterMetadata.Query(name, description, false, OpenApiSchemaMetadata.String()));
        }

        /// <summary>
        /// Declare a list route: the 200 response is an EnumerationResult (schema title "EnumerationResult"), and the
        /// enumeration query parameters are documented. The OpenAPI enforcement test accepts arrays only inside
        /// EnumerationResult schemas or on the reviewed fixed-bound allow-list.
        /// </summary>
        /// <param name="metadata">Metadata.</param>
        /// <param name="itemName">Object type in the objects array, for example FeedPost.</param>
        /// <param name="orderings">Allowed orderings.</param>
        /// <param name="filters">Collection filters, or empty.</param>
        /// <param name="withQueryParameters">Whether to document the GET query parameters (false for POST .../enumerate).</param>
        /// <returns>Metadata.</returns>
        public static OpenApiRouteMetadata Enumeration(this OpenApiRouteMetadata metadata, string itemName, string orderings, string filters = "", bool withQueryParameters = true)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            OpenApiSchemaMetadata item = OpenApiSchemaMetadata.Create("object");
            item.Summary = itemName;
            OpenApiSchemaMetadata schema = OpenApiSchemaMetadata.Create("object");
            schema.Summary = "EnumerationResult";
            schema.Description = "EnumerationResult<" + itemName + ">: success, maxResults, skip, continuationToken, endOfResults, totalRecords, recordsRemaining, timestampUtc, objects.";
            schema.Properties = new System.Collections.Generic.Dictionary<string, OpenApiSchemaMetadata>
            {
                { "continuationToken", OpenApiSchemaMetadata.String() },
                { "endOfResults", OpenApiSchemaMetadata.Boolean() },
                { "objects", OpenApiSchemaMetadata.CreateArray(item) }
            };
            OpenApiRouteMetadata result = metadata.WithResponse(200, OpenApiResponseMetadata.Json("EnumerationResult<" + itemName + ">", schema));
            if (!withQueryParameters) return result;
            return result.EnumerationQueryParams(orderings, filters);
        }

        /// <summary>
        /// Document the GET form of an EnumerationQuery (also used by bounded CSV exports that accept the same query).
        /// </summary>
        /// <param name="metadata">Metadata.</param>
        /// <param name="orderings">Allowed orderings.</param>
        /// <param name="filters">Collection filters, or empty.</param>
        /// <returns>Metadata.</returns>
        public static OpenApiRouteMetadata EnumerationQueryParams(this OpenApiRouteMetadata metadata, string orderings, string filters = "")
        {
            ArgumentNullException.ThrowIfNull(metadata);
            OpenApiRouteMetadata result = metadata
                .QueryParam("maxResults", "Page size (default 25; clamped to the route maximum).")
                .QueryParam("continuationToken", "Token from the previous result.")
                .QueryParam("skip", "Records to skip (0 to 100000; ignored with a token).")
                .QueryParam("ordering", "One of: " + orderings + ".")
                .QueryParam("search", "Free text (1 to 200 characters).")
                .QueryParam("createdAfterUtc", "ISO 8601 lower bound.")
                .QueryParam("createdBeforeUtc", "ISO 8601 upper bound.")
                .QueryParam("labels", "Required labels (repeatable).")
                .QueryParam("labelMatch", "All (default) or Any.")
                .QueryParam("tag.{key}", "Required tag value (repeatable per key; * for any value).");
            foreach (string filter in (filters ?? String.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                result = result.QueryParam(filter, "Collection filter.");
            return result;
        }

        /// <summary>
        /// Declare a single-object JSON response (no unbounded arrays) for the enforcement test.
        /// </summary>
        /// <param name="metadata">Metadata.</param>
        /// <param name="name">Object name, for example FeedPost.</param>
        /// <param name="statusCode">Status code. Default 200.</param>
        /// <returns>Metadata.</returns>
        public static OpenApiRouteMetadata Returns(this OpenApiRouteMetadata metadata, string name, int statusCode = 200)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            OpenApiSchemaMetadata schema = OpenApiSchemaMetadata.Create("object");
            schema.Summary = name;
            return metadata.WithResponse(statusCode, OpenApiResponseMetadata.Json(name, schema));
        }

        /// <summary>
        /// Declare a response that contains a fixed-bound list (allowed by docs/ENUMERATION_AND_METADATA.md section 2.4),
        /// with the bound in the description. The route must also be on the enforcement test's allow-list.
        /// </summary>
        /// <param name="metadata">Metadata.</param>
        /// <param name="name">Object name.</param>
        /// <param name="bound">The documented bound, for example "property reactions" or "at most 20 ids".</param>
        /// <returns>Metadata.</returns>
        public static OpenApiRouteMetadata Bounded(this OpenApiRouteMetadata metadata, string name, string bound)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            OpenApiSchemaMetadata schema = OpenApiSchemaMetadata.Create("object");
            schema.Summary = name;
            schema.Description = "Fixed-bound: " + bound;
            schema.Properties = new System.Collections.Generic.Dictionary<string, OpenApiSchemaMetadata> { { "items", OpenApiSchemaMetadata.CreateArray(OpenApiSchemaMetadata.Create("object")) } };
            return metadata.WithResponse(200, OpenApiResponseMetadata.Json(name + " (" + bound + ")", schema));
        }

        /// <summary>
        /// Declare a non-JSON 200 response (files, CSV pages, HTML) for the enforcement test.
        /// </summary>
        /// <param name="metadata">Metadata.</param>
        /// <param name="contentType">Content type.</param>
        /// <param name="description">Description.</param>
        /// <returns>Metadata.</returns>
        public static OpenApiRouteMetadata File(this OpenApiRouteMetadata metadata, string contentType, string description)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            return metadata.WithResponse(200, OpenApiResponseMetadata.Binary(description, contentType));
        }

        #endregion
    }
}
