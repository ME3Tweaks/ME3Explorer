﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ME3Explorer.AssetDatabase;
using ME3Explorer.AutoTOC;
using ME3Explorer.GameInterop;
using ME3Explorer.Packages;
using ME3Explorer.SharedUI;
using ME3Explorer.Unreal;
using ME3Explorer.Unreal.BinaryConverters;
using Microsoft.Win32;
using SharpDX;
using Path = System.IO.Path;

namespace ME3Explorer.AnimationExplorer
{
    /// <summary>
    /// Interaction logic for AnimationExplorerWPF.xaml
    /// </summary>
    public partial class AnimationExplorerWPF : NotifyPropertyChangedWindowBase
    {
        private const string Me3ExplorerinteropAsiName = "ME3ExplorerInterop.asi";

        public AnimationExplorerWPF()
        {
            ME3ExpMemoryAnalyzer.MemoryAnalyzer.AddTrackedMemoryItem("Animation Viewer", new WeakReference(this));
            DataContext = this;
            InitializeComponent();
            LoadCommands();
            GameController.RecieveME3Message += GameController_RecieveME3Message;
        }

        private void AnimationExplorerWPF_Loaded(object sender, RoutedEventArgs e)
        {
            string dbPath = AssetDB.GetDBPath(MEGame.ME3);
            if (File.Exists(dbPath))
            {
                LoadDatabase(dbPath);
            }
        }

        private void GameController_RecieveME3Message(string msg)
        {
            if (msg == "Loaded string AnimViewer")
            {
                ME3StartingUp = false;
                LoadingAnimation = false;
                IsBusy = false;
                ReadyToView = true;
                animTab.IsSelected = true;
                UpdateLocation();
                UpdateRotation();
                UpdateCameraState();
                this.RestoreAndBringToFront();
            }
        }

        public ObservableCollectionExtended<Animation> Animations { get; } = new ObservableCollectionExtended<Animation>();
        private readonly List<(string fileName, string directory)> FileListExtended = new List<(string fileName, string directory)>();

        private Animation _selectedAnimation;

        public Animation SelectedAnimation
        {
            get => _selectedAnimation;
            set
            {
                if (SetProperty(ref _selectedAnimation, value))
                {
                    LoadAnimation(value);
                }
            }
        }

        private bool _readyToView;
        public bool ReadyToView
        {
            get => _readyToView;
            set => SetProperty(ref _readyToView, value);
        }

        private bool _mE3StartingUp;
        public bool ME3StartingUp
        {
            get => _mE3StartingUp;
            set => SetProperty(ref _mE3StartingUp, value);
        }

        private bool _loadingAnimation;

        public bool LoadingAnimation
        {
            get => _loadingAnimation;
            set => SetProperty(ref _loadingAnimation, value);
        }

        private void SearchBox_OnTextChanged(SearchBox sender, string newtext)
        {
            throw new NotImplementedException();
        }

        #region Busy variables

        private bool _isBusy;

        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        private string _busyText;

        public string BusyText
        {
            get => _busyText;
            set => SetProperty(ref _busyText, value);
        }

        #endregion

        #region Commands

        public RequirementCommand ME3InstalledRequirementCommand { get; set; }
        public RequirementCommand ASILoaderInstalledRequirementCommand { get; set; }
        public RequirementCommand ME3ClosedRequirementCommand { get; set; }
        public RequirementCommand DatabaseLoadedRequirementCommand { get; set; }
        public ICommand StartME3Command { get; set; }
        void LoadCommands()
        {
            ME3InstalledRequirementCommand = new RequirementCommand(IsME3Installed, SelectME3Path);
            ASILoaderInstalledRequirementCommand = new RequirementCommand(IsASILoaderInstalled, OpenASILoaderDownload);
            ME3ClosedRequirementCommand = new RequirementCommand(IsME3Closed, KillME3);
            DatabaseLoadedRequirementCommand = new RequirementCommand(IsDatabaseLoaded, TryLoadDatabase);
            StartME3Command = new GenericCommand(StartME3, AllRequirementsMet);
        }

        private bool IsDatabaseLoaded() => Animations.Any();

        private void TryLoadDatabase()
        {
            string dbPath = AssetDB.GetDBPath(MEGame.ME3);
            if (File.Exists(dbPath))
            {
                LoadDatabase(dbPath);
            }
            else
            {
                MessageBox.Show(this, "Generate an ME3 asset database in the Asset Database tool. This should take about 10 minutes");
            }
        }

        private void LoadDatabase(string dbPath)
        {
            BusyText = "Loading Database...";
            IsBusy = true;
            PropsDataBase db = new PropsDataBase();
            AssetDB.LoadDatabase(dbPath, MEGame.ME3, db).ContinueWithOnUIThread(prevTask =>
            {
                foreach ((string fileName, int dirIndex) in db.FileList)
                {
                    FileListExtended.Add((fileName, db.ContentDir[dirIndex]));
                }
                Animations.AddRange(db.Animations);
                IsBusy = false;
            });
            CommandManager.InvalidateRequerySuggested();
        }

        private bool AllRequirementsMet() => me3InstalledReq.IsFullfilled && asiLoaderInstalledReq.IsFullfilled && me3ClosedReq.IsFullfilled && dbLoadedReq.IsFullfilled;

        private void StartME3()
        {
            BusyText = "Launching Mass Effect 3...";
            IsBusy = true;
            ME3StartingUp = true;
            Task.Run(() =>
            {
                InstallInteropASI();
                AutoTOCWPF.GenerateAllTOCs();

                string animViewerBaseFilePath = Path.Combine(App.ExecFolder, "ME3AnimViewer.pcc");

                using IMEPackage animViewerBase = MEPackageHandler.OpenMEPackage(animViewerBaseFilePath);
                AnimViewer.OpenFileInME3(animViewerBase, false);
            });
        }

        private void InstallInteropASI()
        {
            string interopASIWritePath = GetInteropAsiWritePath();
            if (File.Exists(interopASIWritePath))
            {
                File.Delete(interopASIWritePath);
            }
            File.Copy(Path.Combine(App.ExecFolder, Me3ExplorerinteropAsiName), interopASIWritePath);
        }

        private static string GetInteropAsiWritePath()
        {
            string binariesWin32Dir = Path.GetDirectoryName(ME3Directory.ExecutablePath);
            string asiDir = Path.Combine(binariesWin32Dir, "ASI");
            Directory.CreateDirectory(asiDir);
            string interopASIWritePath = Path.Combine(asiDir, Me3ExplorerinteropAsiName);
            return interopASIWritePath;
        }

        private bool IsME3Closed() => !GameController.TryGetME3Process(out Process me3Process);

        private void KillME3()
        {
            if (GameController.TryGetME3Process(out Process me3Process))
            {
                me3Process.Kill();
            }
            CommandManager.InvalidateRequerySuggested();
        }

        private bool IsASILoaderInstalled()
        {
            if (!IsME3Installed())
            {
                return false;
            }
            string binariesWin32Dir = Path.GetDirectoryName(ME3Directory.ExecutablePath);
            string binkw23Path = Path.Combine(binariesWin32Dir, "binkw23.dll");
            string binkw32Path = Path.Combine(binariesWin32Dir, "binkw32.dll");
            const string binkw23MD5 = "128b560ef70e8085c507368da6f26fe6";
            const string binkw32MD5 = "1acccbdae34e29ca7a50951999ed80d5";

            return File.Exists(binkw23Path) && File.Exists(binkw32Path) && binkw23MD5 == CalculateMD5(binkw23Path) && binkw32MD5 == CalculateMD5(binkw32Path);

            //https://stackoverflow.com/a/10520086
            static string CalculateMD5(string filename)
            {
                using var stream = File.OpenRead(filename);
                using var md5 = MD5.Create();
                byte[] hash = md5.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private void OpenASILoaderDownload()
        {
            Process.Start("https://github.com/Erik-JS/masseffect-binkw32");
        }

        private static bool IsME3Installed() => ME3Directory.ExecutablePath is string exePath && File.Exists(exePath);
        private static void SelectME3Path()
        {
            OpenFileDialog ofd = new OpenFileDialog
            {
                Title = "Select Mass Effect 3 executable.",
                Filter = "MassEffect3.exe|MassEffect3.exe"
            };
            if (ofd.ShowDialog() == true)
            {
                string gamePath = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(ofd.FileName)));

                Properties.Settings.Default.ME3Directory = ME3Directory.gamePath = gamePath;
                Properties.Settings.Default.Save();
                CommandManager.InvalidateRequerySuggested();
            }
        }


        #endregion

        private void LoadAnimation(Animation anim, bool shepard = false)
        {
            if (!LoadingAnimation && GameController.TryGetME3Process(out Process me3Process))
            {
                BusyText = "Loading Animation";
                IsBusy = true;

                if (anim != null && anim.AnimUsages.Any())
                {
                    (int fileListIndex, int animUIndex) = anim.AnimUsages[0];
                    (string filename, string contentdir) = FileListExtended[fileListIndex];
                    string rootPath = ME3Directory.gamePath;

                    filename = $"{filename}.*";
                    var filePath = Directory.GetFiles(rootPath, filename, SearchOption.AllDirectories).FirstOrDefault(f => f.Contains(contentdir));
                    Task.Run(() => AnimViewer.ViewAnimInGame(filePath, animUIndex)).ContinueWithOnUIThread(prevTask => { currentAnimOffsetVector = prevTask.Result; });
                }
                else //no animation selected
                {
                    Task.Run(() =>
                    {
                        string animViewerBaseFilePath = Path.Combine(App.ExecFolder, "ME3AnimViewer.pcc");
                        using IMEPackage animViewerBase = MEPackageHandler.OpenMEPackage(animViewerBaseFilePath);
                        if (shepard)
                        {
                            ExportEntry bioWorldInfo = animViewerBase.GetUExport(4);
                            bioWorldInfo.WriteProperty(new IntProperty(6, "ForcedCasualAppearanceID"));
                        }
                        AnimViewer.OpenFileInME3(animViewerBase, true);
                    });
                }
            }
        }

        private void AnimationExplorerWPF_OnClosing(object sender, CancelEventArgs e)
        {
            if (!GameController.TryGetME3Process(out _))
            {
                string asiPath = GetInteropAsiWritePath();
                if (File.Exists(asiPath))
                {
                    File.Delete(asiPath);
                }
            }
            GameController.RecieveME3Message -= GameController_RecieveME3Message;
            DataContext = null;
        }

        #region Position/Rotation

        private Vector3 currentAnimOffsetVector = Vector3.Zero;

        private int _xPos;
        public int XPos
        {
            get => _xPos;
            set
            {
                if (SetProperty(ref _xPos, value))
                {
                    UpdateLocation();
                }
            }
        }

        private int _yPos;
        public int YPos
        {
            get => _yPos;
            set
            {
                if (SetProperty(ref _yPos, value))
                {
                    UpdateLocation();
                }
            }
        }

        private int _zPos = 70;
        public int ZPos
        {
            get => _zPos;
            set
            {
                if (SetProperty(ref _zPos, value))
                {
                    UpdateLocation();
                }
            }
        }

        private int _yaw = 180;
        public int Yaw
        {
            get => _yaw;
            set
            {
                if (SetProperty(ref _yaw, value))
                {
                    UpdateRotation();
                }
            }
        }

        private int _pitch;
        public int Pitch
        {
            get => _pitch;
            set
            {
                if (SetProperty(ref _pitch, value))
                {
                    UpdateRotation();
                }
            }
        }

        private bool noUpdate;
        private void UpdateLocation()
        {
            if (noUpdate) return;
            GameController.ExecuteME3ConsoleCommands(UpdateFloatVarCommand(XPos, 1),
                                                     UpdateFloatVarCommand(YPos,  2),
                                                     UpdateFloatVarCommand(ZPos, 3), 
                                                     "ce SetActorLocation");
        }

        private void UpdateRotation()
        {
            if (noUpdate) return;

            (float x, float y, float z) = new Rotator(((float)Pitch).ToUnrealRotationUnits(), ((float)Yaw).ToUnrealRotationUnits(), 0).GetDirectionalVector();
            GameController.ExecuteME3ConsoleCommands(UpdateFloatVarCommand(x, 4),
                                                     UpdateFloatVarCommand(y, 5),
                                                     UpdateFloatVarCommand(z, 6),
                                                     "ce SetActorRotation");
        }

        private static string UpdateFloatVarCommand(float value, int index)
        {
            return $"initplotmanagervaluebyindex {index} float {value}";
        }

        private void SetDefaultPosition_Click(object sender, RoutedEventArgs e)
        {
            noUpdate = true;
            XPos = 0;
            YPos = 0;
            ZPos = 70;
            noUpdate = false;
            UpdateLocation();
        }

        private void RemoveAnimationOffset_Click(object sender, RoutedEventArgs e)
        {
            noUpdate = true;
            int x = (int)currentAnimOffsetVector.X;
            int y = (int)currentAnimOffsetVector.Y;
            int z = (int)currentAnimOffsetVector.Z;

            (XPos, YPos, ZPos) = (-x, -y, -z + 70);
            noUpdate = false;
            UpdateLocation();
        }

        private void ResetRotation_Click(object sender, RoutedEventArgs e)
        {
            noUpdate = true;
            Pitch = 0;
            Yaw = 180;
            noUpdate = false;
            UpdateRotation();
        }

        #endregion

        private ECameraState prevCameraState;
        private ECameraState _cameraState;
        public ECameraState CameraState
        {
            get => _cameraState;
            set
            {
                if (SetProperty(ref _cameraState, value))
                {
                    UpdateCameraState();
                    prevCameraState = _cameraState;
                }
            }
        }

        private void UpdateCameraState()
        {
            switch (CameraState)
            {
                //case ECameraState.Fixed when prevCameraState == ECameraState.Shepard:
                //case ECameraState.Free when prevCameraState == ECameraState.Shepard:
                //    LoadAnimation(SelectedAnimation);
                //    break;
                case ECameraState.Fixed when prevCameraState == ECameraState.Free:
                case ECameraState.Free:
                    GameController.ExecuteME3ConsoleCommands("toggledebugcamera");
                    break;
                //case ECameraState.Shepard when prevCameraState != ECameraState.Shepard:
                //    LoadAnimation(SelectedAnimation, true);
                //    break;
            }
        }

        private void QuiteME3_Click(object sender, RoutedEventArgs e)
        {
            KillME3();
        }
    }

    public enum ECameraState
    {
        Fixed,
        Free,
        //Shepard
    }
}
