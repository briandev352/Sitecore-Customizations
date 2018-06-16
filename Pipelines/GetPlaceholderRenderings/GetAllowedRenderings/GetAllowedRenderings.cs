using Sitecore;
using Sitecore.Caching.Placeholders;
using Sitecore.Data;
using Sitecore.Data.Fields;
using Sitecore.Data.Items;
using Sitecore.Diagnostics;
using Sitecore.Layouts;
using Sitecore.Pipelines.GetPlaceholderRenderings;
using Sitecore.SecurityModel;
using Sitecore.Text;
using Sitecore.Web;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Website.Pipelines.GetPlaceholderRenderings
{
    /// <summary>
    /// Pipeline for getting the list of renderings allowed in a specific placeholder.
    /// </summary>
    public class GetAllowedRenderings
    {
        /// <summary>
        /// Pipeline entry point.
        /// </summary>
        /// <param name="args">The pipeline arguments.</param>
        public void Process(GetPlaceholderRenderingsArgs args)
        {
            Assert.IsNotNull(args, nameof(args));

            IEnumerable<Item> placeholderItems;
            if (ID.IsNullOrEmpty(args.DeviceId))
            {
                // if the DevideId is null fall back to base behavior
                placeholderItems = new[] {
                    Client.Page.GetPlaceholderItem(args.PlaceholderKey, args.ContentDatabase, args.LayoutDefinition)
                };
            }
            else
            {
                using (new DeviceSwitcher(args.DeviceId, args.ContentDatabase))
                {
                    // Finds all the placeholder settings definitions in the context item's layout field that
                    // reference the specified placeholder key
                    placeholderItems = GetPlaceholderItems(args.PlaceholderKey, args.ContentDatabase, args.LayoutDefinition);
                }
            }

            // Loop over the placeholder settings items and collect a list of all Allowed Controls
            List<Item> allowedRenderings = new List<Item>();
            if (placeholderItems != null && placeholderItems.Any())
            {
                bool allowedControlsSpecified = false;
                args.HasPlaceholderSettings = true;

                foreach (Item placeholderItem in placeholderItems)
                {
                    // Get the list of rendrings defined in the 'Allowed Controls' field
                    var renderings = GetAllowedRenderingsByPlaceholder(placeholderItem);
                    if (renderings != null && renderings.Any())
                    {
                        // Add the renderings from the Allowed Controls field of this placeholder
                        // settings item to the result set
                        allowedControlsSpecified = true;
                        allowedRenderings.AddRange(renderings);
                    }
                }

                // When no renderings are defined for a placeholder, the default Sitecore behavioer is
                // to show the Sitecore tree. If at least one rendering was found in Allowed Controls
                // then we tell Sitecore not to show the tree.
                if (allowedControlsSpecified)
                {
                    args.Options.ShowTree = false;
                }
            }

            // Save the collected Allowed Controls to pipeline args
            if (allowedRenderings.Any()) {
                if (args.PlaceholderRenderings == null)
                {
                    args.PlaceholderRenderings = new List<Item>();
                }

                // Ensure the list of renderings doesn't have any duplicates. Use an equality comparer for
                // Sitecore.Data.Items.Item which compares item IDs instead of object references.
                args.PlaceholderRenderings.AddRange(allowedRenderings.Distinct(new ItemEqualityComparer()));
            }
        }

        /// <summary>
        /// Gets the list of renderings defined as 'Allowed Controls" for the specified
        /// placeholder settings item.
        /// </summary>
        /// <param name="placeholderItem">The placeholder settings item.</param>
        /// <returns>The list of allowed renderings.</returns>
        public static IEnumerable<Item> GetAllowedRenderingsByPlaceholder(Item placeholderItem)
        {
            Assert.ArgumentNotNull(placeholderItem, nameof(placeholderItem));

            // Reads the 'Allowed Controls' field
            ListString listString = new ListString(placeholderItem["Allowed Controls"]);
            if (listString.Count <= 0)
            {
                return null;
            }

            // Converts the raw value from the 'Allowed Controls' field to a list of actual Items
            // by calling to the database
            List<Item> renderings = new List<Item>();
            foreach (string path in listString)
            {
                Item obj = placeholderItem.Database.GetItem(path);
                if (obj != null)
                {
                    renderings.Add(obj);
                }
            }

            return renderings;
        }

        /// <summary>
        /// Gets the list of placeholder settings items attached to the context item for the
        /// specified placeholder key.
        /// </summary>
        /// <param name="placeholderKey">The placeholder key.</param>
        /// <param name="database">The database context.</param>
        /// <returns>The list of placeholder settings items.</returns>
        public static IEnumerable<Item> GetPlaceholderItems(string placeholderKey, Database database)
        {
            Assert.ArgumentNotNull(placeholderKey, nameof(placeholderKey));
            Assert.ArgumentNotNull(database, nameof(database));

            // If nothing has been defined in the 'Layout' field then fall back to base functionality
            string layoutDefinition = null;
            if (Context.PageDesigner.IsDesigning)
            {
                string pageDesignerHandle = Context.PageDesigner.PageDesignerHandle;
                if (!string.IsNullOrEmpty(pageDesignerHandle))
                {
                    layoutDefinition = WebUtil.GetSessionString(pageDesignerHandle);
                }
            }

            Item item = Context.Item;
            if (item != null && string.IsNullOrEmpty(layoutDefinition))
            {
                layoutDefinition = new LayoutField(item).Value;
            }

            // placeholder data is stored in the Layout fiels, do if the context item does not have any
            // layout information then we can return null
            if (string.IsNullOrEmpty(layoutDefinition))
            {
                return null;
            }

            return GetPlaceholderItems(placeholderKey, database, layoutDefinition);
        }

        /// <summary>
        /// Gets the list of placeholder settings items attached to the context item for the
        /// specified placeholder key.
        /// </summary>
        /// <param name="placeholderKey">The placeholder key.</param>
        /// <param name="database">The database context.</param>
        /// <param name="layoutDefinition">The context item's layout definition.</param>
        /// <returns>The list of placeholder settings items.</returns>
        public static IEnumerable<Item> GetPlaceholderItems(string placeholderKey, Database database, string layoutDefinition)
        {
            Assert.ArgumentNotNull(placeholderKey, nameof(placeholderKey));
            Assert.ArgumentNotNull(database, nameof(database));
            Assert.ArgumentNotNull(layoutDefinition, nameof(layoutDefinition));

            // Gets the list of placeholder definitions defined in the 'Layout' field of the context item
            placeholderKey = placeholderKey.ToLowerInvariant();
            IEnumerable<PlaceholderDefinition> placeholderDefinitions = GetPlaceholderDefinitionList(layoutDefinition, placeholderKey);
            List<Item> placeholderItems = new List<Item>();

            // Maps the placeholder definitions to a list of placeholder settings items by calling to the database
            if (placeholderDefinitions != null && placeholderDefinitions.Any())
            {
                foreach (var placeholderDefinition in placeholderDefinitions)
                {
                    string metaDataItemId = placeholderDefinition.MetaDataItemId;
                    if (!string.IsNullOrEmpty(metaDataItemId)) {
                        using (new SecurityDisabler())
                            placeholderItems.Add(database.GetItem(metaDataItemId));
                    }
                }

                return placeholderItems.Distinct(new ItemEqualityComparer());
            }
            // If there are no placeholder definitions then fall back to base behavior
            else
            {
                PlaceholderCache placeholderCache = PlaceholderCacheManager.GetPlaceholderCache(database.Name);
                Item obj = placeholderCache[placeholderKey];
                if (obj != null)
                    return new[] { obj };
                int num = placeholderKey.LastIndexOf('/');
                if (num >= 0)
                {
                    string index = StringUtil.Mid(placeholderKey, num + 1);
                    obj = placeholderCache[index];
                }
                return obj == null ? Enumerable.Empty<Item>() : new[] { obj };
            }
        }

        /// <summary>
        /// Gets the list of placeholder definitions from a Layout field.
        /// </summary>
        /// <param name="definition">The raw value of the Layout field.</param>
        /// <param name="placeholderKey">The placeholder key.</param>
        /// <returns>The list of placeholder definitions.</returns>
        public static IEnumerable<PlaceholderDefinition> GetPlaceholderDefinitionList(string definition, string placeholderKey)
        {
            Assert.ArgumentNotNull(definition, nameof(definition));
            Assert.ArgumentNotNull(placeholderKey, nameof(placeholderKey));
            return GetPlaceholderDefinitionList(LayoutDefinition.Parse(definition), placeholderKey, Context.IsUnitTesting
                ? new ID("{FE5D7FDF-89C0-4D99-9AA3-B5FBD009C9F3}")
                : Context.Device.ID);
        }

        /// <summary>
        /// Gets the list of placeholder definitions from a Layout field.
        /// </summary>
        /// <param name="definition">The parsed Layout field.</param>
        /// <param name="placeholderKey">The placeholder key.</param>
        /// <param name="deviceId">The device ID.</param>
        /// <returns>The list of placeholder definitions.</returns>
        public static IEnumerable<PlaceholderDefinition> GetPlaceholderDefinitionList(LayoutDefinition definition, string placeholderKey, ID deviceId)
        {
            Assert.ArgumentNotNull(definition, nameof(definition));
            Assert.ArgumentNotNull(placeholderKey, nameof(placeholderKey));
            Assert.ArgumentNotNull(deviceId, nameof(deviceId));

            // Gets the list of placeholder definitions for the specified device
            ArrayList placeholders = definition.GetDevice(deviceId.ToString()).Placeholders;
            List<PlaceholderDefinition> placeholderDefinitions = new List<PlaceholderDefinition>();

            if (placeholders == null || placeholders.Count == 0)
            {
                return null;
            }

            PlaceholderDefinition placeholderDefinitionTemp = null;
            string lastPart = StringUtil.GetLastPart(placeholderKey, '/', placeholderKey);

            // Search through all the placeholder definitions to find all the ones that have a matching key
            foreach (PlaceholderDefinition placeholderDefinition in placeholders)
            {
                string key = placeholderDefinition.Key;
                if (string.Equals(key, lastPart, StringComparison.InvariantCultureIgnoreCase) || string.Equals(key, placeholderKey, StringComparison.InvariantCultureIgnoreCase))
                {
                    placeholderDefinitions.Add(placeholderDefinition);
                }
            }

            return placeholderDefinitions;
        }
    }
}