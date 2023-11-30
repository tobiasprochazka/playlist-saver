using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO;
using VideoLibrary;
using Google.Apis.Auth.OAuth2;
using Google.Apis.YouTube.v3;
using System.Threading;
using Google.Apis.Util.Store;
using Google.Apis.Services;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Linq;


namespace playlist_saver
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private FileInfo[] files;
        private ListBoxItem line;
        private string selectedItemId;
        private readonly string apikey = "";
        private readonly string appName = "playlistsaver";
        //pagetoken needed for more than 50 results
        private readonly int maxResults = 50;
        private List<CustomListBoxItem> customListBoxItems;

        public MainWindow()
        {
            InitializeComponent();
        }

        //
        // TODO
        //
        // SEPARATE METHOD FOR COLORING DOWNLOADED ITEMS AFTER DOWNLOAD FINISHES - USES TOO MANY REQUESTS
        
        // look for online updates and update listbox accordingly 
        // faster download

        private void Button_Click_GetOnlineItems(object sender, RoutedEventArgs e)
        {

            if (listBoxOffline.Items.Count == 0)
                FillOfflineListBox();
            FillOnlineListBox();
        }


        private void ItemsButton_Click_GetOfflineItems(object sender, RoutedEventArgs e)
        {        
            FillOfflineListBox();
        }


        private void ListBoxOnline_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ListBox listBoxSender = (ListBox)sender;
            ListBoxItem selectedItem = (ListBoxItem)listBoxSender.SelectedItem;
            
            if(selectedItem != null)
            {
                string selectedItemTitle = selectedItem.Content.ToString();
                
                selectedItemTitle = selectedItemTitle.Substring(selectedItemTitle.IndexOf(" ") + 1);
                CustomListBoxItem item = customListBoxItems.Find(r => r.CurrentTitle == selectedItemTitle);
                
                if(item != null)
                {
                    selectedItemId = item.CurrentID.ToString();
                }

                //if (selectedItemTitle.Contains("id: "))
                //{
                //    selectedItemId = selectedItemTitle.Substring(selectedItemTitle.LastIndexOf("id: ") + 4);
                //}
                //else
                //{
                //    selectedItemId = "";
                //}

            }
        }

        private async void Button_Click_Download(object sender, RoutedEventArgs e)
        {
            if (selectedItemId == "")
            {
                MessageBox.Show("Video already downloaded", "download",MessageBoxButton.OK);
            }
            else if(selectedItemId == null)
            {
                MessageBox.Show("Select video from online repository first", "download", MessageBoxButton.OK);
            }
            else
            {
                if (MessageBox.Show("Download song?", "download", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    Uri uri = new Uri("https://www.youtube.com/watch?v=" + selectedItemId);
                    var progress = new Progress<double>(percent =>
                    {
                       downloadProgressBar.Value = percent;
                    });

                    await DownloadAudio(uri, textBoxPathInput.Text, progress);
                }
            }
        }



        private async Task DownloadAudio(Uri uri, string path, IProgress<double> progress)
        {
            //kinda slow
            
            var youtube = YouTube.Default;
            var findAudio = youtube.GetAllVideos(uri.ToString());
            var audios = findAudio.Where(_ => _.AudioFormat == AudioFormat.Aac && _.AdaptiveKind == AdaptiveKind.Audio).ToList();
            YouTubeVideo audio = audios.FirstOrDefault(x => x.AudioBitrate > 0);
            
            downloadProgressLabel.Content = "downloading: " + audio.Title;
            
            long? totalByte = 0;
            double percent = 0;
            string filename = audio.Title;
            filename = string.Join("_", filename.Split(System.IO.Path.GetInvalidFileNameChars()));
            string finalPath = path + filename + ".m4a";
            var client = new HttpClient();

            using (Stream output = File.OpenWrite(finalPath))
            {
                using (var request = new HttpRequestMessage(HttpMethod.Head, audio.Uri))
                {
                    totalByte = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).Result.Content.Headers.ContentLength;
                }
                using (var input = await client.GetStreamAsync(audio.Uri))
                {
                    byte[] buffer = new byte[16 * 1024];
                    int read;
                    int totalRead = 0;
                    while ((read = await input.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        output.Write(buffer, 0, read);
                        totalRead += read;
                        percent = (double)totalRead / (double)totalByte * 100;
                        progress.Report(percent);
                        
                    }
                }
            }

            downloadProgressLabel.Content = "download finished: " + audio.Title;
            
            
            string artUrlString = uri.ToString();
            CustomListBoxItem item = customListBoxItems.Find(x => x.CurrentID.Contains(artUrlString.Replace("https://www.youtube.com/watch?v=","")));
            SetAlbumArt(finalPath, new Uri(item.CurrentThumbnail));
            SetPositionCommentMetadata(item.CurrentPosition, finalPath);
            FillOfflineListBox();
            await FillOnlineListBox();
        }

        private void SetPositionCommentMetadata(long position, string path)
        {
            TagLib.File mp3File = TagLib.File.Create(path);
            mp3File.Tag.Comment = position.ToString();
            mp3File.Save();
        }

        private void SetAlbumArt(string path, Uri imageUri)
        {
            byte[] imageBytes;
            using (System.Net.WebClient client = new System.Net.WebClient())
            {
                imageBytes = client.DownloadData(imageUri);
            }
       
            File.WriteAllBytes(@"C:\Users\Toby\Music\backup\cover.jpg", imageBytes);

            TagLib.File file = TagLib.File.Create(path);
            //TagLib.Picture picture = new TagLib.Picture(imageBytes);
            TagLib.Picture picture = new TagLib.Picture(@"C:\Users\Toby\Music\backup\cover.jpg");
            //cover
            file.Tag.Pictures = new TagLib.IPicture[] { picture };
            file.Save();
            
        }

        private async Task FillOnlineListBox()
        {
            listBoxOnline.Items.Clear();

            var youtubeService = new YouTubeService(new Google.Apis.Services.BaseClientService.Initializer()
            {
                ApiKey = apikey,
                ApplicationName = appName
            });

            //creating list
            customListBoxItems = new List<CustomListBoxItem>();

            var nextPageToken = "";
            while (nextPageToken != null)
            {

                var playlistItemsListRequest = youtubeService.PlaylistItems.List("snippet");
                playlistItemsListRequest.PlaylistId = textBoxPlaylistIdInput.Text;
                playlistItemsListRequest.MaxResults = maxResults;
                playlistItemsListRequest.PageToken = nextPageToken;
                var playlistItemsListResponse = await playlistItemsListRequest.ExecuteAsync();

                foreach (var playlistItem in playlistItemsListResponse.Items)
                {
                    var currentVideoTitle = playlistItem.Snippet.Title;
                    currentVideoTitle = string.Join("_", currentVideoTitle.Split(System.IO.Path.GetInvalidFileNameChars()));
                    var currentVideoId = playlistItem.Snippet.ResourceId.VideoId;
                    //sometimes videos dont have standard thumbnail
                    var thumbnail = playlistItem.Snippet.Thumbnails;
                    string currentVideoThumbnail = "";
                    if (thumbnail.Standard != null)
                        currentVideoThumbnail = thumbnail.Standard.Url;
                    else if (thumbnail.Medium != null)
                        currentVideoThumbnail = thumbnail.Medium.Url;
                    else if (thumbnail.High != null)
                        currentVideoThumbnail = thumbnail.High.Url;
                    else if (thumbnail.Maxres != null)
                        currentVideoThumbnail = thumbnail.Maxres.Url;

                    long position = (long)playlistItem.Snippet.Position;

                    customListBoxItems.Add(new CustomListBoxItem() { 
                        CurrentTitle = currentVideoTitle, 
                        CurrentThumbnail = currentVideoThumbnail, 
                        CurrentID = currentVideoId, 
                        CurrentPosition = position 
                    });

                    //if there are no videos
                    if (files != null)
                    {
                        //if video doesnt exist offline
                        if (!files.Any(x => x.Name.Replace(x.Extension, "") == currentVideoTitle))
                        {
                            line = new ListBoxItem { Content = position + ". " + currentVideoTitle };
                            listBoxOnline.Items.Add(line);
                        }
                        //already downloaded
                        else
                        {
                            line = new ListBoxItem { Content = position + ". " + currentVideoTitle, Foreground = Brushes.Green };
                            listBoxOnline.Items.Add(line);
                        }
                    }
                    else
                    {
                        line = new ListBoxItem { Content = position + ". " + currentVideoTitle };
                        listBoxOnline.Items.Add(line);
                    }
                }
                nextPageToken = playlistItemsListResponse.NextPageToken;
            }
            
        }

        private void FillOfflineListBox()
        {
            listBoxOffline.Items.Clear();
            
            DirectoryInfo d = new DirectoryInfo(textBoxPathInput.Text);

            files = d.GetFiles("*.m4a");
            string[] names = new string[files.Count()];

            if (customListBoxItems != null)
            {
                foreach(FileInfo fileInfo in files)
                {
                    string name = fileInfo.Name.Replace(fileInfo.Extension, "");
                    CustomListBoxItem item = customListBoxItems.Find(r => r.CurrentTitle == name);
                    if(item != null)
                    {
                        TagLib.File taglibFile = TagLib.File.Create(fileInfo.FullName);
                        taglibFile.Tag.Comment = item.CurrentPosition.ToString();
                        taglibFile.Save();

                    }
                }
            }


            for (int i  = 0; i < files.Length; i++)
            {
                TagLib.File mp3File = TagLib.File.Create(files[i].FullName);
                string comment = mp3File.Tag.Comment;
                mp3File.Dispose();
                string finalText = comment + ". " + files[i].Name.Replace(files[i].Extension, "");
                names[i] = finalText;
            }
            Array.Sort(names, new NumericStringComparer());

            foreach(string name in names)
            {
                line = new ListBoxItem { Content = name };
                listBoxOffline.Items.Add(line);
            }

            //foreach (FileInfo file in files)
            //{
            //    TagLib.File mp3File = TagLib.File.Create(file.FullName);
            //    string comment = mp3File.Tag.Comment;
            //    mp3File.Dispose();
            //    string finalText = comment + ". " + file.Name.Replace(file.Extension, "");
            //    names[int.Parse(comment)] = finalText;
            //    Array.Sort(names);
                
            //    line = new ListBoxItem { Content = names[i] };
            //    listBoxOffline.Items.Add(line);
            //}
        }
    }

    public class CustomListBoxItem
    {
        public string CurrentTitle { get; set; }
        public string CurrentID { get; set; }
        public string CurrentThumbnail { get; set; }
        public long CurrentPosition {  get; set; }
    }

    public class NumericStringComparer : IComparer<string>
    {
        public int Compare(string x, string y)
        {
            // Extract numeric values from the start of the strings
            int xNum = ExtractNumericValue(x);
            int yNum = ExtractNumericValue(y);

            // Compare numeric values
            return xNum.CompareTo(yNum);
        }

        private int ExtractNumericValue(string str)
        {
            // Find the first non-numeric character
            int endIndex = 0;
            while (endIndex < str.Length && char.IsDigit(str[endIndex]))
            {
                endIndex++;
            }

            // Extract and parse the numeric part of the string
            if (int.TryParse(str.Substring(0, endIndex), out int numericValue))
            {
                return numericValue;
            }
            else
            {
                // Default to 0 if no numeric value found
                return 0;
            }
        }
    }
}
