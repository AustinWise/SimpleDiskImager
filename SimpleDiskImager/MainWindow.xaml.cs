using Austin.ThumbWriter;
using Austin.ThumbWriter.DiskImages;
using Austin.ThumbWriter.VirtualDiskService;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;

namespace SimpleDiskImager
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        WindowInteropHelper mInteropHealper;
        HwndSource mHwndSource;
        Storyboard mWiggleStory;

        private bool mShouldWiggleOnDiskLoad;
        private VdsSession mVdsSession;
        private Task mDiskLoadingTask = Task.CompletedTask;
        private bool mWritingImage;
        private List<Disk> mExistingDisks;

        public MainWindow()
        {
            InitializeComponent();
            this.IsEnabled = false;

            mWiggleStory = (Storyboard)cmbDisk.FindResource("ComboWiggleStory");
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                mVdsSession = await VdsSession.CreateAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Startup Failure", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(1);
                return;
            }

            mInteropHealper = new WindowInteropHelper(this);
            mHwndSource = HwndSource.FromHwnd(mInteropHealper.Handle);
            mHwndSource.AddHook(hwndHook);

            QueueDiskLoading();
        }

        private async Task LoadDisks(Task _)
        {
            Debug.Assert(this.Dispatcher.Thread == Thread.CurrentThread);

            if (mWritingImage)
                return;

            this.IsEnabled = false;
            prog.IsIndeterminate = true;

            try
            {
                var disks = await mVdsSession.GetDisksAsync();

                //Check to see if the enumerated disks are any different than the last time we checked.
                if (mExistingDisks?.Count == disks.Count)
                {
                    bool allEqual = true;
                    for (int i = 0; i < disks.Count; i++)
                    {
                        if (!disks[i].Equals(mExistingDisks[i]))
                        {
                            allEqual = false;
                            break;
                        }
                    }

                    if (allEqual)
                    {
                        //All the disks are the same as the last time we checked, so don't bother updating.
                        //If we don't do this, this wiggle animation does not look as cool.
                        return;
                    }
                }

                mExistingDisks = disks;
                cmbDisk.ItemsSource = disks.Where(d => d.IsRemovable).ToArray();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Disk Enumeration Failure", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(1);
                return;
            }
            finally
            {
                prog.IsIndeterminate = false;
                this.IsEnabled = true;
            }

            if (mShouldWiggleOnDiskLoad)
            {
                mWiggleStory.Begin();
            }
            else
            {
                mShouldWiggleOnDiskLoad = true;
            }
        }

        private void QueueDiskLoading()
        {
            Debug.Assert(this.Dispatcher.Thread == Thread.CurrentThread);
            mDiskLoadingTask = mDiskLoadingTask.ContinueWith(LoadDisks, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void BrowseForImage_Click(object sender, RoutedEventArgs e)
        {
            var formats = DiskImageFactory.GetAllSupportedFormats();

            var filter = new StringBuilder();
            filter.Append("All Supported Images Types|");
            filter.Append(string.Join(";", formats.Select(f => "*" + f.Extension)));
            foreach (var format in formats)
            {
                filter.Append('|');
                filter.Append(format.Name);
                filter.Append('|');
                filter.Append("*" + format.Extension);
            }

            var dlg = new OpenFileDialog();
            dlg.Title = "Open Disk Image";
            dlg.Filter = filter.ToString();
            if (dlg.ShowDialog() == true)
            {
                txtImagePath.Text = dlg.FileName;
            }
        }

        private async void WriteImage_Click(object sender, RoutedEventArgs e)
        {
            string imagePath = txtImagePath.Text;
            Disk disk = (Disk)cmbDisk.SelectedItem;

            if (!File.Exists(imagePath))
            {
                MessageBox.Show(this, "Disk image file does not exist.");
                return;
            }

            if (disk == null)
            {
                MessageBox.Show(this, "No disk was selected.");
                return;
            }

            var confirmMessage = new StringBuilder();
            confirmMessage.AppendLine("Are you sure you want to OVERWRITE and DESTROY all data on:");
            confirmMessage.AppendLine(disk.Name);
            confirmMessage.AppendLine("With the contents of:");
            confirmMessage.AppendLine(imagePath);

            if (MessageBoxResult.Yes != MessageBox.Show(this, confirmMessage.ToString(), "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning, defaultResult: MessageBoxResult.No))
            {
                return;
            }

            prog.Value = 0;

            mWritingImage = true;
            this.IsEnabled = false;

            try
            {
                using (var diskImage = DiskImageFactory.Create(imagePath))
                {
                    await DiskImageWriter.WriteImageToDisk(disk, diskImage, new Progress<int>(onProgress));
                }
                MessageBox.Show(this, "Image writing complete!");
            }
            catch (PartitionInformationMissingException)
            {
                var sb = new StringBuilder();
                sb.AppendLine("No partitioning information was found in in this disk image.");
                sb.AppendLine("If this is an ISO image, such as a Windows install disc image,");
                sb.AppendLine("please try using Rufus instead.");
                sb.AppendLine();
                sb.Append("Would you like to navigate to the Rufus web site?");
                var result = MessageBox.Show(this, sb.ToString(), this.Title, MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    Process.Start("https://rufus.ie/");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to write image: " + ex.Message);
            }
            finally
            {
                mWritingImage = false;
                mShouldWiggleOnDiskLoad = false;
                this.IsEnabled = true;
                QueueDiskLoading();
            }
        }

        private void onProgress(int progress)
        {
            prog.Value = progress;
        }

        IntPtr hwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_DEVICECHANGE = 0x0219;
            if (msg == WM_DEVICECHANGE)
            {
                var DBT_DEVNODES_CHANGED = new IntPtr(0x0007);
                var DBT_DEVICEARRIVAL = new IntPtr(0x8000);
                var DBT_DEVICEREMOVECOMPLETE = new IntPtr(0x8004);
                if (wParam == DBT_DEVNODES_CHANGED || wParam == DBT_DEVICEARRIVAL || wParam == DBT_DEVICEREMOVECOMPLETE)
                {
                    QueueDiskLoading();
                }
            }
            return IntPtr.Zero;
        }
    }
}
