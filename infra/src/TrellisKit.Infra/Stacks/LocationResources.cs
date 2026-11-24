namespace TrellisKit.Infra.Stacks
{
    using System;
    using Amazon.CDK;
    using Amazon.CDK.AWS.IAM;
    using Amazon.CDK.AWS.Location;
    using TrellisKit.Infra.Config;
    using Constructs;

    /// <summary>
    /// One property's Amazon Location Service resources (location section of deploy/infra.json): a place index
    /// {prefix}-places (data provider configurable, intended use Storage by default) with geo:SearchPlaceIndexForText for
    /// the task role, and an optional map resource {prefix}-map with the geo:GetMap* actions. Created only when the
    /// property opts in, so a deployment without it has no Location resource or permission. Nothing here sets the live
    /// directory settings; the outputs list what an administrator enters in the admin console.
    /// </summary>
    public class LocationResources : Construct
    {
        /// <summary>
        /// Actions the task role receives on the map resource (every geo:GetMap* action, listed explicitly).
        /// </summary>
        public static readonly string[] MapActions = new string[] { "geo:GetMapGlyphs", "geo:GetMapSprites", "geo:GetMapStyleDescriptor", "geo:GetMapTile" };

        /// <summary>
        /// The place index, or null when not enabled.
        /// </summary>
        public CfnPlaceIndex? PlaceIndex { get; }

        /// <summary>
        /// The map resource, or null when not enabled.
        /// </summary>
        public CfnMap? Map { get; }

        /// <summary>
        /// Instantiate the resources.
        /// </summary>
        /// <param name="scope">Parent construct (the Api stack).</param>
        /// <param name="id">Construct id.</param>
        /// <param name="property">Configuration of the property that owns them.</param>
        /// <param name="taskRole">Task role of the server.</param>
        /// <param name="outputSuffix">Suffix of the output names (empty in the per-property topology).</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public LocationResources(Construct scope, string id, EnvironmentConfig property, IRole taskRole, string outputSuffix) : base(scope, id)
        {
            ArgumentNullException.ThrowIfNull(property);
            ArgumentNullException.ThrowIfNull(taskRole);
            ArgumentNullException.ThrowIfNull(outputSuffix);

            LocationSettings settings = property.Location;
            string region = Stack.Of(this).Region;

            if (settings.PlaceIndexEnabled)
            {
                PlaceIndex = new CfnPlaceIndex(this, "PlaceIndex", new CfnPlaceIndexProps
                {
                    IndexName = property.ResourceName("places"),
                    DataSource = settings.DataProvider,
                    DataSourceConfiguration = new CfnPlaceIndex.DataSourceConfigurationProperty { IntendedUse = settings.IntendedUse },
                    Description = property.DisplayName + " directory geocoder (" + property.Name + ")"
                });
                taskRole.AddToPrincipalPolicy(new PolicyStatement(new PolicyStatementProps
                {
                    Sid = "LocationSearch" + outputSuffix,
                    Actions = new string[] { "geo:SearchPlaceIndexForText" },
                    Resources = new string[] { PlaceIndex.AttrArn }
                }));
                new CfnOutput(Stack.Of(this), "PlaceIndexName" + outputSuffix, new CfnOutputProps
                {
                    Value = property.ResourceName("places"),
                    Description = "Admin console: directory.geocoder.provider AwsLocation, directory.geocoder.placeIndexName this value, directory.geocoder.region " + region + "."
                });
            }

            if (settings.MapEnabled)
            {
                Map = new CfnMap(this, "Map", new CfnMapProps
                {
                    MapName = property.ResourceName("map"),
                    Configuration = new CfnMap.MapConfigurationProperty { Style = settings.MapStyle },
                    Description = property.DisplayName + " directory map (" + property.Name + ")"
                });
                taskRole.AddToPrincipalPolicy(new PolicyStatement(new PolicyStatementProps
                {
                    Sid = "LocationMap" + outputSuffix,
                    Actions = MapActions,
                    Resources = new string[] { Map.AttrArn }
                }));
                new CfnOutput(Stack.Of(this), "MapStyleUrl" + outputSuffix, new CfnOutputProps
                {
                    Value = "https://maps.geo." + region + ".amazonaws.com/maps/v0/maps/" + property.ResourceName("map") + "/style-descriptor",
                    Description = "Admin console: directory.map.provider MapLibre, directory.map.styleUrl this value plus ?key=<API key>, directory.map.publicKey the Amazon Location API key (created out of band, restricted to this map and the website origins)."
                });
            }
        }
    }
}
