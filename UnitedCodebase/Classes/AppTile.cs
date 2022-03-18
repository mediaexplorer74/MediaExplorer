using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Core;
using Windows.Data.Xml.Dom;
using Windows.Foundation.Metadata;
using Windows.UI.Notifications;
using Windows.UI.Shell;
using Windows.UI.StartScreen;

//based on Mvvm.Services
namespace UnitedCodebase.Classes
{
    public class AppTile
    {
        public static bool IsPinToTaskBarEnabled => ApiInformation.IsTypePresent("Windows.UI.Shell.TaskbarManager") && TaskbarManager.GetDefault().IsPinningAllowed;

        public static bool IsPinToStartMenuEnabled
        {
            get
            {
                if (ApiInformation.IsTypePresent("Windows.UI.StartScreen.StartScreenManager"))
                {
                    return Task.Run<bool>(() => IsPinToStartMenuSupported()).Result;
                }

                return false;
            }
        }

        private static async Task<bool> IsPinToStartMenuSupported()
        {
            AppListEntry entry = (await Package.Current.GetAppListEntriesAsync())[0];
            return StartScreenManager.GetDefault().SupportsAppListEntry(entry);
        }

        public async static Task<bool?> IsPinnedToTaskBar()
        {
            if (IsPinToTaskBarEnabled)
            {
                return await TaskbarManager.GetDefault().IsCurrentAppPinnedAsync();
            }
            else
            {
                return null;
            }
        }

        public static async Task<bool?> RequestPinToTaskBar()
        {
            if (IsPinToTaskBarEnabled)
            {
                return await TaskbarManager.GetDefault().RequestPinCurrentAppAsync();
            }
            else
            {
                return null;
            }
        }

        public static async Task<bool?> IsPinnedToStartMenu()
        {
            if (IsPinToStartMenuEnabled)
            {
                AppListEntry entry = (await Package.Current.GetAppListEntriesAsync())[0];
                return await StartScreenManager.GetDefault().ContainsAppListEntryAsync(entry);
            }
            else
            {
                return null;
            }
        }

        public static async Task<bool?> RequestPinToStartMenu()
        {
            if (IsPinToStartMenuEnabled)
            {
                AppListEntry entry = (await Package.Current.GetAppListEntriesAsync())[0];
                return await StartScreenManager.GetDefault().RequestAddAppListEntryAsync(entry);
            }
            else
            {
                return null;
            }
        }

        public async static Task<bool> HasSecondaryTiles()
        {
            var tiles = await SecondaryTile.FindAllAsync();
            return tiles.Count > 0;
        }

        public async static Task<List<string>> SecondaryTilesIds()
        {
            var tiles = await SecondaryTile.FindAllAsync();
            var result = new List<string>();
            foreach (var tile in tiles)
            {
                if (!string.IsNullOrEmpty(tile.TileId))
                {
                    result.Add(tile.TileId);
                }
            }

            return result;
        }

        public async static Task<bool> RequestPinSecondaryTile(string tileName, string displayname, Uri imageUri = null)
        {
            if(imageUri == null)
            {
                imageUri = new Uri("ms-appx:///Assets/Square150x150Logo.scale-100.png");
            }

            if (!SecondaryTile.Exists(tileName))
            {
                SecondaryTile tile = new SecondaryTile(
                    SanitizedTileName(displayname).Replace("!",""),
                    displayname,
                    SanitizedTileName(tileName),
                    imageUri,
                    TileSize.Default);
                tile.VisualElements.ShowNameOnSquare150x150Logo = true;
                return await tile.RequestCreateAsync();
            }

            return true; // Tile existed already.
        }

        public async static Task<bool> RequestUnPinSecondaryTile(string tileName)
        {
            if (SecondaryTile.Exists(tileName))
            {
                return await new SecondaryTile(tileName).RequestDeleteAsync();
            }

            return true; // Tile did not exist.
        }


        public async static Task<bool> RequestPinSecondaryTileToTaskbar(string tileName, string displayname)
        {
            if (ApiInformation.IsMethodPresent("Windows.UI.Shell.TaskbarManager", "RequestPinSecondaryTileAsync"))
            {
                // API present!
                // Unlock the pin to taskbar feature
                var result = LimitedAccessFeatures.TryUnlockFeature(
                    "com.microsoft.windows.secondarytilemanagement",
                    "<tokenFromMicrosoft>",
                    "<publisher> has registered their use of com.microsoft.windows.secondarytilemanagement with Microsoft and agrees to the terms of use.");

                // If unlock succeeded
                if ((result.Status == LimitedAccessFeatureStatus.Available) ||
                    (result.Status == LimitedAccessFeatureStatus.AvailableWithoutToken))
                {
                    if (!SecondaryTile.Exists(tileName))
                    {
                        SecondaryTile tile = new SecondaryTile(SanitizedTileName(displayname).Replace("!", ""))
                        {
                            DisplayName = displayname,
                            Arguments = SanitizedTileName(tileName)
                        };
                        tile.VisualElements.Square44x44Logo = new Uri("ms-appx:///Assets/Square44x44Logo.scale-100.png");
                        tile.VisualElements.Square150x150Logo = new Uri("ms-appx:///Assets/Square150x150Logo.scale-100.png");
                        tile.VisualElements.ShowNameOnSquare150x150Logo = true;
                        return await TaskbarManager.GetDefault().RequestPinSecondaryTileAsync(tile);
                    }
                }
            }

            return true;          
        }

        private static string SanitizedTileName(string tileName)
        {
            // TODO: complete if necessary...
            return tileName.Replace(" ", "_");
        }

        #region "Tile Badge"

        public static void SetBadgeNumber(int num)
        {

            // Get the blank badge XML payload for a badge number
            XmlDocument badgeXml =
                BadgeUpdateManager.GetTemplateContent(BadgeTemplateType.BadgeNumber);

            // Set the value of the badge in the XML to our number
            XmlElement badgeElement = badgeXml.SelectSingleNode("/badge") as XmlElement;
            badgeElement.SetAttribute("value", num.ToString());

            // Create the badge notification
            BadgeNotification badge = new BadgeNotification(badgeXml);

            // Create the badge updater for the application
            BadgeUpdater badgeUpdater =
                BadgeUpdateManager.CreateBadgeUpdaterForApplication();

            // And update the badge
            badgeUpdater.Update(badge);

        }

        public static void SetBadgeGlyph(string glyph)
        {
            // Get the blank badge XML payload for a badge glyph
            XmlDocument badgeXml =
                BadgeUpdateManager.GetTemplateContent(BadgeTemplateType.BadgeGlyph);

            // Set the value of the badge in the XML to our glyph value
            XmlElement badgeElement =
                badgeXml.SelectSingleNode("/badge") as XmlElement;
            badgeElement.SetAttribute("value", glyph);

            // Create the badge notification
            BadgeNotification badge = new BadgeNotification(badgeXml);

            // Create the badge updater for the application
            BadgeUpdater badgeUpdater =
                BadgeUpdateManager.CreateBadgeUpdaterForApplication();

            // And update the badge
            badgeUpdater.Update(badge);

        }

        public static void ClearBadge()
        {
            BadgeUpdateManager.CreateBadgeUpdaterForApplication().Clear();
        }
        #endregion
    }
}
