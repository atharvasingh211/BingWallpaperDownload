﻿using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.IO;
using System.Net;
using Windows.System.UserProfile;
using Windows.Storage;
using Windows.Graphics.Display;
using System.Net.Http;

namespace BBCore
{
    /// <summary>
    /// State of function RunFunctionAsync may return.
    /// </summary>
    public enum RunFunctionCode
    {
        SUCCESSFUL, FAILED, NO_INTERNET, UNEXPECTED_EXCEPTION
    }

    /// <summary>
    /// The core of Bing background app.  It would download and set images from Bing to desktop as wallpapers.
    /// </summary>
    public class Core
    {
        #region Properties

        /// <summary>
        /// The key of last date stored in local settings.
        /// </summary>
        public static string LastDateKey { get { return "lastDate"; } }

        /// <summary>
        /// The default resolution extension for downloading image.
        /// </summary>
        public static string DefaultResolutionExtension { get { return "_" + DefaultWidthByHeight + ".jpg"; } }

        /// <summary>
        /// Get string of today's DateTime
        /// </summary>
        public static string GetDateString { get { return DateTime.Now.ToString("M-d-yyyy"); } }

        /// <summary>
        /// Flag on if or not the resolutionExtension is set.  Use resolutionExtension if yes.
        /// </summary>
        private bool IsResolutionExtensionSet { get; set; }

        /// <summary>
        /// The resolution extension of the image, in the format of "_1920x1080.jpg".
        /// </summary>
        private string ResolutionExtension { get; set; }

        /// <summary>
        /// The token to pick folder which stores images.
        /// </summary>
        private string PickFolderToken { get { return "PickedFolderToken"; } }
        private static string DefaultWidthByHeight { get { return "1920x1080"; } }

        private string WidthByHeight
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_widthByHeight))
                {
                    _widthByHeight = DefaultWidthByHeight;
                }
                return _widthByHeight;
            }
        }

        #endregion

        /// <summary>
        /// The defualt subdirectory images stored in local.
        /// </summary>
        private const string DEFAULT_IMAGES_SUBDIRECTORY = "DownloadedImages";

        private string _widthByHeight;

        #region Constructors

        /// <summary>
        /// Constructor with default isResolutionExtensionSet = false;
        /// </summary>
        public Core()
        {
            IsResolutionExtensionSet = false;
        }

        /// <summary>
        /// Constructor with resolutionExtension and isResolutionExtensionSet = true;
        /// </summary>
        /// <param name="resolutionExtension"></param>
        public Core(string resolutionExtension)
        {
            this.ResolutionExtension = resolutionExtension;
            IsResolutionExtensionSet = true;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Download and set images from Bing as wallpapers.
        /// </summary>
        /// <param name="imagesSubdirectory">Subdirectory of images.  Default as "DownloadedImages".</param>
        /// <returns>RunFunctionCode represents the result of running.</returns>
        public async Task<RunFunctionCode> RunAsync(string imagesSubdirectory = DEFAULT_IMAGES_SUBDIRECTORY)
        {
            RunFunctionCode value;
            if (!IsResolutionExtensionSet)
            {
                SetWidthByHeight(); // Set
            }
            try
            {
                string urlBase = await GetBackgroundUrlBaseAsync().ConfigureAwait(false);
                if (!IsResolutionExtensionSet)
                {
                    ResolutionExtension = await GetResolutionExtensionAsync(urlBase).ConfigureAwait(false);
                }
                string address = await DownloadWallpaperAsync(urlBase + ResolutionExtension, GetFileName(), imagesSubdirectory).ConfigureAwait(false);
                var result = await SetWallpaperAsync(address).ConfigureAwait(false);
                if (result)
                {
                    ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                    localSettings.Values[LastDateKey] = GetDateString;
                    value = RunFunctionCode.SUCCESSFUL; // Wallpaper set successful!
                }
                else
                {
                    value = RunFunctionCode.FAILED; // Wallpaper set failed!
                }
            }
            catch (WebException)
            {
                value = RunFunctionCode.NO_INTERNET;    // Find Internet connection problem!";
            }
            //catch (Exception)
            //{
            //    value = RunFunctionCode.UNEXPECTED_EXCEPTION;   // Unexpected Exception!
            //}

            return value;
        }

        /// <summary>
        /// Get the folder which user wants to sotre images.
        /// </summary>
        /// <returns>The folder which user wants to store images.</returns>
        public async Task<StorageFolder> GetFolderAsync()
        {
            StorageFolder folder;
            try
            {
                folder = await Windows.Storage.AccessCache.StorageApplicationPermissions.
                    FutureAccessList.GetFolderAsync(PickFolderToken);
            }
            catch (ArgumentException)
            {
                folder = await SetFolderAsync().ConfigureAwait(false);
            }
            return folder;
        }

        /// <summary>
        /// Set the folder to store images with FolderPicker.
        /// </summary>
        /// <returns>The folder user choose.</returns>
        public async Task<StorageFolder> SetFolderAsync()
        {
            var folderPicker = new Windows.Storage.Pickers.FolderPicker();
            folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
            folderPicker.FileTypeFilter.Add("*");

            StorageFolder folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                // Application now has read/write access to all contents in the picked folder
                // (including other sub-folder contents)
                Windows.Storage.AccessCache.StorageApplicationPermissions.
                FutureAccessList.AddOrReplace(PickFolderToken, folder);
                //this.textBlock.Text = "Picked folder: " + folder.Name;
            }
            else
            {
                //this.textBlock.Text = "Operation cancelled.";
            }
            return folder;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Download, from bing, JSON file which includes information of image address.
        /// </summary>
        /// <returns>Deserialized JSON file</returns>
        private async Task<dynamic> DownloadJsonAsync()
        {
            //            using (WebClient webClient = new WebClient())
            using (var client = new HttpClient())
            {
                Console.WriteLine("Downloading JSON...");
                //              webClient.Encoding = System.Text.Encoding.UTF8;
                //            string jsonString = webClient.DownloadString("https://www.bing.com/HPImageArchive.aspx?format=js&idx=0&n=1&mkt=en-UK");
                var uri = new Uri("https://www.bing.com/HPImageArchive.aspx?format=js&idx=0&n=1&mkt=en-UK");
                var jsonString = await client.GetStringAsync(uri).ConfigureAwait(false);
                return JsonConvert.DeserializeObject<dynamic>(jsonString);
            }
        }

        /// <summary>
        /// Get URL base of the image without resolution extension
        /// </summary>
        /// <returns>the URL base</returns>
        private async Task<string> GetBackgroundUrlBaseAsync()
        {
            dynamic jsonObject = await DownloadJsonAsync().ConfigureAwait(false);
            return "https://www.bing.com" + jsonObject.images[0].urlbase;
        }

        /// <summary>
        /// Test if the website with given URL exists.
        /// </summary>
        /// <param name="url">The URL of the website</param>
        /// <returns>If the website exists.</returns>
        private async Task<bool> WebsiteExistsAsync(string url)
        {
            try
            {
                WebRequest request = WebRequest.Create(url);
                request.Method = "HEAD";
                
                HttpWebResponse response = (HttpWebResponse) await request.GetResponseAsync().ConfigureAwait(false);
                return response.StatusCode == HttpStatusCode.OK;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get the resolution extension, given URL, of images.
        /// </summary>
        /// <param name="url">URL of images.</param>
        /// <returns>The resolution extension.</returns>
        private async Task<string> GetResolutionExtensionAsync(string url)
        {
            string potentialExtension = "_" + WidthByHeight + ".jpg";
            if (await WebsiteExistsAsync(url + potentialExtension).ConfigureAwait(false))
            {
                Console.WriteLine("Background for " + WidthByHeight + " found.");
                return potentialExtension;
            }
            else
            {
                Console.WriteLine("No background for " + WidthByHeight + " was found.");
                Console.WriteLine("Using 1920x1080 instead.");
                return DefaultResolutionExtension;
            }
        }

        /// <summary>
        /// Get the name of file it should be today.
        /// </summary>
        /// <returns>The name of file.</returns>
        private string GetFileName()
        {
            return GetDateString + ".bmp";
        }

        /// <summary>
        /// Download the image from a given URL and store it with a specified file name under certain subdirectory.
        /// </summary>
        /// <param name="url">URL of the image.</param>
        /// <param name="fileName">The name of the file.</param>
        /// <param name="ImagesSubdirectory">Subdirectory location to store files.</param>
        /// <returns>Path of the image file.</returns>
        private async Task<string> DownloadWallpaperAsync(string url, string fileName, string ImagesSubdirectory)
        {
            var rootFolder = await GetFolderAsync();
            StorageFile storageFile;
            try
            {
                storageFile = await rootFolder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
            }
            catch (Exception)
            {
                storageFile = await rootFolder.CreateFileAsync(fileName, CreationCollisionOption.OpenIfExists);
            }

            using (HttpClient client = new HttpClient())
            {
                byte[] buffer = await client.GetByteArrayAsync(url).ConfigureAwait(false);
                using (Stream stream = await storageFile.OpenStreamForWriteAsync().ConfigureAwait(false))
                    stream.Write(buffer, 0, buffer.Length);
            }

            // Use this path to load image
            string newPath = string.Format("ms-appdata:///local/{0}/{1}", ImagesSubdirectory, fileName);
            var file = await ApplicationData.Current.LocalFolder.CreateFolderAsync(ImagesSubdirectory, CreationCollisionOption.OpenIfExists);
            try
            {
                await storageFile.CopyAsync(file);
            }
            catch (Exception)
            {
                // TODO print file already exist
            }
            return newPath;
        }

        /// <summary>
        /// Set the image from given URI as wallpaper.
        /// </summary>
        /// <param name="localAppDataFileName">URI of the image.</param>
        /// <returns>If or not wallpaper is set successed.</returns>
        private async Task<bool> SetWallpaperAsync(string localAppDataFileName)
        {
            bool success = false;
            if (UserProfilePersonalizationSettings.IsSupported())
            {
                var uri = new Uri(localAppDataFileName);
                StorageFile file = await StorageFile.GetFileFromApplicationUriAsync(uri);
                UserProfilePersonalizationSettings profileSettings = UserProfilePersonalizationSettings.Current;
                success = await profileSettings.TrySetWallpaperImageAsync(file);
            }
            return success;
        }

        private void SetWidthByHeight()
        {
            _widthByHeight = DisplayInformation.GetForCurrentView().ScreenWidthInRawPixels
                + "x" + DisplayInformation.GetForCurrentView().ScreenHeightInRawPixels;
        }

        #endregion
    }
}