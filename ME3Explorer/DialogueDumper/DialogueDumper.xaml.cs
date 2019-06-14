﻿/**
 * Dialogue Dumper is based on ME3Tweaks Mass Effect 3 Mod Manager Command Line Tools
 * TransplanterLib. This is a modified version provided by Mgamerz
 * (c) Mgamerz 2019
 */

using ClosedXML.Excel;
using ME3Explorer.Packages;
using ME3Explorer.SharedUI;
using ME3Explorer.Unreal;
using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Windows;
using System.Windows.Input;
using static ME3Explorer.TlkManagerNS.TLKManagerWPF;

namespace ME3Explorer.DialogueDumper
{
    /// <summary>
    /// Interaction logic for Window1.xaml
    /// </summary>
    public partial class DialogueDumper : NotifyPropertyChangedWindowBase
    {
        /// <summary>
        /// Items show in the list that are currently being processed
        /// </summary>
        public ObservableCollectionExtended<DialogueDumperSingleFileTask> CurrentDumpingItems { get; set; } = new ObservableCollectionExtended<DialogueDumperSingleFileTask>();

        /// <summary>
        /// All items in the queue
        /// </summary>
        private List<DialogueDumperSingleFileTask> AllDumpingItems;

        public XLWorkbook workbook = new XLWorkbook();
        private static BackgroundWorker xlworker = new BackgroundWorker();
        public BlockingCollection<List<string>> _xlqueue = new BlockingCollection<List<string>>();
        private ActionBlock<DialogueDumperSingleFileTask> ProcessingQueue;
        private string outputfile;

        private int _listViewHeight;
        public int ListViewHeight
        {
            get => _listViewHeight;
            set => SetProperty(ref _listViewHeight, value);
        }

        private void LoadCommands()
        {
            // Player commands
            DumpME1Command = new RelayCommand(DumpGameME1, CanDumpGameME1);
            DumpME2Command = new RelayCommand(DumpGameME2, CanDumpGameME2);
            DumpME3Command = new RelayCommand(DumpGameME3, CanDumpGameME3);
            DumpSpecificFilesCommand = new RelayCommand(DumpSpecificFiles, CanDumpSpecificFiles);
            CancelDumpCommand = new RelayCommand(CancelDump, CanCancelDump);
            ManageTLKsCommand = new RelayCommand(ManageTLKs);
        }

        private async void DumpSpecificFiles(object obj)
        {
            CommonOpenFileDialog dlg = new CommonOpenFileDialog
            {
                Multiselect = true,
                EnsureFileExists = true,
                Title = "Select files to dump",
            };
            dlg.Filters.Add(new CommonFileDialogFilter("All supported files", "*.pcc;*.sfm;*.u;*.upk"));
            dlg.Filters.Add(new CommonFileDialogFilter("Mass Effect package files", "*.sfm;*.u;*.upk"));
            dlg.Filters.Add(new CommonFileDialogFilter("Mass Effect 2/3 package files", "*.pcc"));


            if (dlg.ShowDialog(this) == CommonFileDialogResult.Ok)
            {
                CommonSaveFileDialog outputDlg = new CommonSaveFileDialog
                {
                    Title = "Select excel output",
                    DefaultFileName = "DialogueDump.xlsx",
                    DefaultExtension = "xlsx",
                };
                outputDlg.Filters.Add(new CommonFileDialogFilter("Excel Files", "*.xlsx"));

                if (outputDlg.ShowDialog(this) == CommonFileDialogResult.Ok)
                {
                    outputfile = outputDlg.FileName;
                    await dumpPackages(dlg.FileNames.ToList(), outputfile);
                }
            }
            this.RestoreAndBringToFront();
        }

        /// <summary>
        /// Cancelation of dumping
        /// </summary>
        private bool DumpCanceled;

        /// <summary>
        /// used to monitor process
        /// </summary>
        private bool isProcessing;

        /// <summary>
        /// output debug info to excel
        /// </summary>
        public bool shouldDoDebugOutput;

        #region commands
        public ICommand DumpME1Command { get; set; }
        public ICommand DumpME2Command { get; set; }
        public ICommand DumpME3Command { get; set; }
        public ICommand DumpSpecificFilesCommand { get; set; }
        public ICommand CancelDumpCommand { get; set; }
        public ICommand ManageTLKsCommand { get; set; }

        private int _overallProgressValue;
        public int OverallProgressValue
        {
            get => _overallProgressValue;
            set => SetProperty(ref _overallProgressValue, value);
        }

        private int _overallProgressMaximum;
        public int OverallProgressMaximum
        {
            get => _overallProgressMaximum;
            set => SetProperty(ref _overallProgressMaximum, value);
        }

        private string _currentOverallOperationText;
        public string CurrentOverallOperationText
        {
            get => _currentOverallOperationText;
            set => SetProperty(ref _currentOverallOperationText, value);
        }

        private bool CanDumpSpecificFiles(object obj)
        {
            return (ProcessingQueue == null || ProcessingQueue.Completion.Status != TaskStatus.WaitingForActivation) && !isProcessing;
        }

        private bool CanDumpGameME1(object obj)
        {
            return ME1Directory.gamePath != null && Directory.Exists(ME1Directory.gamePath) && (ProcessingQueue == null || ProcessingQueue.Completion.Status != TaskStatus.WaitingForActivation) && !isProcessing;
        }

        private bool CanDumpGameME2(object obj)
        {
            return ME2Directory.gamePath != null && Directory.Exists(ME2Directory.gamePath) && (ProcessingQueue == null || ProcessingQueue.Completion.Status != TaskStatus.WaitingForActivation) && !isProcessing;
        }

        private bool CanDumpGameME3(object obj)
        {
            return ME3Directory.gamePath != null && Directory.Exists(ME3Directory.gamePath) && (ProcessingQueue == null || ProcessingQueue.Completion.Status != TaskStatus.WaitingForActivation) && !isProcessing;
        }

        private bool CanCancelDump(object obj)
        {
            return ((ProcessingQueue != null && ProcessingQueue.Completion.Status == TaskStatus.WaitingForActivation) || isProcessing) && !DumpCanceled;
        }

        private void CancelDump(object obj)
        {
            DumpCanceled = true;
            AllDumpingItems?.ForEach(x => x.DumpCanceled = true);
            CommandManager.InvalidateRequerySuggested(); //Refresh commands
        }

        private void DumpGameME1(object obj)
        {
            DumpGame(MEGame.ME1);
        }

        private void DumpGameME2(object obj)
        {
            DumpGame(MEGame.ME2);
        }

        private void DumpGameME3(object obj)
        {
            DumpGame(MEGame.ME3);
        }

        private static void ManageTLKs(object obj)
        {
           var tlkmgr = new TlkManagerNS.TLKManagerWPF();
           tlkmgr.Show();
        }

        #endregion

        public DialogueDumper(Window owner = null)
        {
            ME3ExpMemoryAnalyzer.MemoryAnalyzer.AddTrackedMemoryItem("Dialogue Dumper", new WeakReference(this));
            Owner = owner;
            LoadCommands();
            ListViewHeight = 25 * App.CoreCount;
            InitializeComponent();
        }

        private void DumpGame(MEGame game)
        {
            string rootPath = null;
            switch (game)
            {
                case MEGame.ME1:
                    rootPath = ME1Directory.gamePath;
                    break;
                case MEGame.ME2:
                    rootPath = ME2Directory.gamePath;
                    break;
                case MEGame.ME3:
                    rootPath = ME3Directory.gamePath;
                    break;
            }
            CommonSaveFileDialog m = new CommonSaveFileDialog
            {
                Title = "Select excel output",
                DefaultFileName = $"{game}DialogueDump.xlsx",
                DefaultExtension = "xlsx",
            };
            m.Filters.Add(new CommonFileDialogFilter("Excel Files", "*.xlsx"));

            if (m.ShowDialog(this) == CommonFileDialogResult.Ok)
            {
                outputfile = m.FileName;
                this.RestoreAndBringToFront();
                dumpPackagesFromFolder(rootPath, outputfile, game);
            }
        }

        private async void DialogueDumper_FilesDropped(object sender, DragEventArgs e)
        {
            if (ProcessingQueue != null && ProcessingQueue.Completion.Status == TaskStatus.WaitingForActivation) { return; } //Busy

            CommonSaveFileDialog outputDlg = new CommonSaveFileDialog
            {
                Title = "Select excel output",
                DefaultFileName = "DialogueDump.xlsx",
                DefaultExtension = "xlsx",
            };
            outputDlg.Filters.Add(new CommonFileDialogFilter("Excel Files", "*.xlsx"));

            if (outputDlg.ShowDialog(this) == CommonFileDialogResult.Ok)
            {
                outputfile = outputDlg.FileName;
            }
            this.RestoreAndBringToFront();

            OverallProgressValue = 0;
            OverallProgressMaximum = 100;
            CurrentOverallOperationText = "Scanning...";

            var filenames = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (filenames.Length == 1 && Directory.Exists(filenames[0]))
            {
                //Directory - can drop
                dumpPackagesFromFolder(filenames[0], outputfile);
            }
            else
            {
                await dumpPackages(filenames.ToList(), outputfile);
            }
        }

        /// <summary>
        /// Dumps PCC data from all PCCs in the specified folder, recursively.
        /// </summary>
        /// <param name="path">Base path to start dumping functions from. Will search all subdirectories for package files.</param>
        /// <param name="game">MEGame game determines.  If default then Unknown, which means done as single file (always fully parsed). </param>
        /// <param name="outputfile">Output Excel document.</param>
        public async void dumpPackagesFromFolder(string path, string outFile, MEGame game = MEGame.Unknown)
        {
            OverallProgressValue = 0;
            OverallProgressMaximum = 100;
            CurrentOverallOperationText = "Scanning...";
            await Task.Delay(100);  //allow dialog catch up before i/o

            path = Path.GetFullPath(path);
            var supportedExtensions = new List<string> { ".u", ".upk", ".sfm", ".pcc" };
            List<string> files = Directory.GetFiles(path, "Bio*.*", SearchOption.AllDirectories).Where(s => supportedExtensions.Contains(Path.GetExtension(s.ToLower()))).ToList();
            await dumpPackages(files, outFile, game);
        }

        private async Task dumpPackages(List<string> files, string outFile, MEGame game = MEGame.Unknown)
        {
            CurrentOverallOperationText = "Dumping packages...";
            OverallProgressMaximum = files.Count;
            OverallProgressValue = 0;
            isProcessing = true;
            workbook = new XLWorkbook();
#if DEBUG
            shouldDoDebugOutput = false;  //Output for toolset devs
#endif

            var xlstrings = workbook.Worksheets.Add("TLKStrings");
            var xlowners = workbook.Worksheets.Add("ConvoOwners");

            //Setup column headers
            xlstrings.Cell(1, 1).Value = "Speaker";
            xlstrings.Cell(1, 2).Value = "TLK StringRef";
            xlstrings.Cell(1, 3).Value = "Line";
            xlstrings.Cell(1, 4).Value = "Conversation";
            xlstrings.Cell(1, 5).Value = "Game";
            xlstrings.Cell(1, 6).Value = "File";
            xlstrings.Cell(1, 7).Value = "Object #";

            xlowners.Cell(1, 1).Value = "Conversation";
            xlowners.Cell(1, 2).Value = "Owner";
            xlowners.Cell(1, 3).Value = "File";

            if(shouldDoDebugOutput) //DEBUG
            {
                var xltags = workbook.Worksheets.Add("Tags");
                xltags.Cell(1, 1).Value = "ActorTag";
                xltags.Cell(1, 2).Value = "StrRef";
                xltags.Cell(1, 3).Value = "FriendlyName";

                var xldebug = workbook.Worksheets.Add("DEBUG");
                xldebug.Cell(1, 1).Value = "Status";
                xldebug.Cell(1, 2).Value = "File";
                xldebug.Cell(1, 3).Value = "Class";
                xldebug.Cell(1, 4).Value = "Uexport";
                xldebug.Cell(1, 5).Value = "e";
            }

            _xlqueue = new BlockingCollection<List<string>>(); //Reset queue for multiple operations

            //Background Consumer does excel work
            xlworker = new BackgroundWorker();
            xlworker.DoWork += XlProcessor;
            xlworker.RunWorkerCompleted += Xlworker_RunWorkerCompleted;
            xlworker.WorkerSupportsCancellation = true;
            xlworker.RunWorkerAsync();

            ProcessingQueue = new ActionBlock<DialogueDumperSingleFileTask>(x =>
            {
                if (x.DumpCanceled)
                {
                    OverallProgressValue++;
                    return;
                }
                Application.Current.Dispatcher.Invoke(() => CurrentDumpingItems.Add(x));
                x.dumpPackageFile(game, this); // What to do on each item
                Application.Current.Dispatcher.Invoke(() =>
                {
                    OverallProgressValue++; //Concurrency 
                    CurrentDumpingItems.Remove(x);
                });
            }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = App.CoreCount }); // How many items at the same time 

            AllDumpingItems = new List<DialogueDumperSingleFileTask>();
            CurrentDumpingItems.ClearEx();
            foreach (var item in files)
            {
                var threadtask = new DialogueDumperSingleFileTask(item);
                AllDumpingItems.Add(threadtask); //For setting cancelation value
                ProcessingQueue.Post(threadtask); // Post all items to the block
                
            }

            ProcessingQueue.Complete(); // Signal completion
            CommandManager.InvalidateRequerySuggested();
            await ProcessingQueue.Completion;

            if(!shouldDoDebugOutput)
            {
                CurrentOverallOperationText = $"Dump {(DumpCanceled ? "canceled" : "completed")} - saving excel";
            }

            while (isProcessing)
            {
                isProcessing = await CheckProcess();
            }
        }

        public async Task<bool> CheckProcess()
        {
            if ( _xlqueue.IsEmpty() && ((OverallProgressValue >= OverallProgressMaximum) || DumpCanceled))
            {
                _xlqueue.CompleteAdding();
                return false;
            }

            await Task.Delay(1000);
            return true;
        }

        private void Xlworker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            xlworker.CancelAsync();
            CommandManager.InvalidateRequerySuggested();
            try
            {
                workbook.SaveAs(outputfile);
                if (DumpCanceled)
                {
                    DumpCanceled = false;
                    OverallProgressMaximum = 100;
                    MessageBox.Show("Dialogue Dump was cancelled. Work in progress was saved.", "Cancelled", MessageBoxButton.OK);
                    CurrentOverallOperationText = "Dump canceled";
                }
                else
                {
                    OverallProgressValue = 100;
                    OverallProgressMaximum = 100;
                    MessageBox.Show("Dialogue Dump was completed.", "Success", MessageBoxButton.OK);
                    CurrentOverallOperationText = "Dump completed";
                }
                
            }
            catch
            {
                MessageBox.Show("Unable to save excel file. Check it is not open.", "Error", MessageBoxButton.OK);
            }
        }

        private void XlProcessor(object sender, DoWorkEventArgs e)
        {
            foreach (List<string> newrow in _xlqueue.GetConsumingEnumerable(CancellationToken.None))
            {
                try
                {
                    string sheetName = newrow[0]; 
                    var activesheet = workbook.Worksheet(sheetName);
                    int nextrow = activesheet.LastRowUsed().RowNumber() + 1;
                    //Write output to excel
                    for (int s = 1; s < newrow.Count; s++)
                    {
                        string val = newrow[s];
                        activesheet.Cell(nextrow, s).Value = val;
                    }
                }
                catch
                {
                    MessageBox.Show($"Error writing excel. {newrow[0]}, {newrow[2]}, {newrow[6]}, {newrow[7]}");
                }
            }
        }

        private void DialogueDumper_Closing(object sender, CancelEventArgs e)
        {
            DumpCanceled = true;
            AllDumpingItems?.ForEach(x => x.DumpCanceled = true);
        }

        private void DialogueDumper_Loaded(object sender, RoutedEventArgs e)
        {
            Owner = null; //Detach from parent
        }

        private void Dump_BackgroundThread(object sender, DoWorkEventArgs e)
        {
            var (rootPath, outputDir) = (ValueTuple<string, string>)e.Argument;
        }

        private void Dump_Completed(object sender, RunWorkerCompletedEventArgs e)
        {
            try
            {
                var result = e.Result;
            }
            catch (Exception ex)
            {
                var exceptionMessage = ExceptionHandlerDialogWPF.FlattenException(ex);
                Debug.WriteLine(exceptionMessage);
            }
        }

        private void DialogueDumper_DragOver(object sender, DragEventArgs e)
        {
            if (ProcessingQueue != null && ProcessingQueue.Completion.Status == TaskStatus.WaitingForActivation)
            {
                //Busy
                e.Effects = DragDropEffects.None;
                e.Handled = true;
            }

            bool dropEnabled = true;
            if (e.Data.GetDataPresent(DataFormats.FileDrop, true))
            {
                var filenames = (string[])e.Data.GetData(DataFormats.FileDrop, true);
                if (filenames.Length == 1 && Directory.Exists(filenames[0]))
                {
                    //Directory - can drop
                }
                else
                {

                    string[] acceptedExtensions = { ".pcc", ".u", ".upk", ".sfm" };
                    foreach (string filename in filenames)
                    {
                        string extension = Path.GetExtension(filename).ToLower();
                        if (!acceptedExtensions.Contains(extension))
                        {
                            dropEnabled = false;
                            break;
                        }
                    }
                }
            }
            else
            {
                dropEnabled = false;
            }

            if (!dropEnabled)
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
            }
        }
    }

    public class DialogueDumperSingleFileTask : NotifyPropertyChangedBase
    {
        private string _currentOverallOperationText;
        public string CurrentOverallOperationText
        {
            get => _currentOverallOperationText;
            set => SetProperty(ref _currentOverallOperationText, value);
        }

        private int _currentFileProgressValue;
        public int CurrentFileProgressValue
        {
            get => _currentFileProgressValue;
            set => SetProperty(ref _currentFileProgressValue, value);
        }

        private int _currentFileProgressMaximum;
        public int CurrentFileProgressMaximum
        {
            get => _currentFileProgressMaximum;
            set => SetProperty(ref _currentFileProgressMaximum, value);
        }

        private string _shortFileName;
        public string ShortFileName
        {
            get => _shortFileName;
            set => SetProperty(ref _shortFileName, value);
        }

        public DialogueDumperSingleFileTask(string file)
        {
            File = file;
            ShortFileName = Path.GetFileNameWithoutExtension(file);
            CurrentOverallOperationText = $"Dumping {ShortFileName}";
        }

        public bool DumpCanceled;
        
        private readonly string File;

        /// <summary>
        /// Dumps Conversation strings to xl worksheet
        /// </summary>
        /// <workbook>Output excel workbook</workbook>
        public void dumpPackageFile(MEGame GameBeingDumped, DialogueDumper dumper)
        {
            string fileName = ShortFileName.ToUpper();
            if (dumper.shouldDoDebugOutput)
            {
                dumper.CurrentOverallOperationText = $"Dumping Packages.... {dumper.OverallProgressValue}/{dumper.OverallProgressMaximum} { dumper._xlqueue.Count.ToString() }";
                var excelout = new List<string> { "DEBUG", "IN PROCESS", fileName };
                dumper._xlqueue.Add(excelout);
            } 

            //SETUP FILE FILTERS
            bool CheckConv;
            bool CheckActor;

            if (GameBeingDumped == MEGame.Unknown) //Unknown = Single files or folders that always fully parse
            {
                CheckConv = true;
                CheckActor = true;
            }
            else if (GameBeingDumped == MEGame.ME1 && !fileName.EndsWith(@"LOC_INT") && !fileName.EndsWith(@"LAY") && !fileName.EndsWith(@"SND") && !fileName.EndsWith(@"_T") && !fileName.StartsWith(@"BIOG") && !fileName.StartsWith(@"BIOC"))
            {
                CheckConv = true; //Filter ME1 remove file types that never have convos. Levels only.
                CheckActor = true;
            }
            else if (GameBeingDumped != MEGame.ME1 && fileName.EndsWith(@"LOC_INT")) //Filter ME2/3 files with potential convos
            {
                CheckConv = true;
                CheckActor = false;
            }
            else if (GameBeingDumped != MEGame.ME1 && !fileName.EndsWith(@"LOC_INT") && !fileName.StartsWith(@"BIOG")) //Filter ME2/3 files with potential actors
            {
                CheckConv = false;
                CheckActor = true;
            }
            else //Otherwise skip file
            {
                CurrentFileProgressValue = CurrentFileProgressMaximum;
                if (dumper.shouldDoDebugOutput)
                {
                    var excelout = new List<string> { "DEBUG", "SKIPPED", fileName };
                    dumper._xlqueue.Add(excelout);
                }
                return;
            }

            string className = null;
            try
            {
                using (IMEPackage pcc = MEPackageHandler.OpenMEPackage(File))
                {
                    if (GameBeingDumped == MEGame.Unknown) //Correct mapping
                    {
                        GameBeingDumped = pcc.Game;
                    }

                    CurrentFileProgressMaximum = pcc.ExportCount;
                    
                    //CHECK FOR CONVERSATIONS TO DUMP
                    if (CheckConv)
                    {

                        foreach (IExportEntry exp in pcc.Exports)
                        {
                            if (DumpCanceled)
                            {
                                return;
                            }
                            CurrentFileProgressValue = exp.UIndex;

                            className = exp.ClassName;
                            if (className == "BioConversation")
                            {
                                string convName = exp.ObjectName;
                                int convIdx = exp.UIndex;


                                var convo = exp.GetProperties();
                                if (convo.Count > 0)
                                {
                                    //1.  Define speaker list "m_aSpeakerList"
                                    List<string> speakers = new List<string>();
                                    if (GameBeingDumped != MEGame.ME3)
                                    {
                                        var s_speakers = exp.GetProperty<ArrayProperty<StructProperty>>("m_SpeakerList");
                                        if (s_speakers != null)
                                        {
                                            foreach (StructProperty s in s_speakers)
                                            {
                                                var n = s.GetProp<NameProperty>("sSpeakerTag");
                                                speakers.Add(n.ToString());
                                            }
                                        }

                                    }
                                    else
                                    {
                                        var a_speakers = exp.GetProperty<ArrayProperty<NameProperty>>("m_aSpeakerList");
                                        if (a_speakers != null)
                                        {
                                            foreach (NameProperty n in a_speakers)
                                            {
                                                speakers.Add(n.ToString());
                                            }
                                        }
                                    }

                                    //If ME1 Setup Local talkfile
                                    //Package files will load this natively now

                                    //ME1Explorer.Unreal.Classes.TalkFile locTlk = null;
                                    //if (GameBeingDumped == MEGame.ME1)
                                    //{
                                    //    byte[] lnkTLKbinary = pcc.getUExport(exp.GetProperty<ObjectProperty>("m_oTlkFileSet").Value).getBinaryData();
                                    //    int offset = 0;
                                    //    int tlkcount = BitConverter.ToInt32(lnkTLKbinary, offset);
                                    //    for (int t = 0; t < tlkcount; t++)
                                    //    {
                                    //        offset = 4 + t * 20;
                                    //        int n = BitConverter.ToInt32(lnkTLKbinary, offset);
                                    //        if (exp.FileRef.findName("Int") == n)
                                    //        {
                                    //            offset += 16;
                                    //            break;
                                    //        }
                                    //    }

                                    //    int indexTlk = BitConverter.ToInt32(lnkTLKbinary, offset);
                                    //    locTlk = new ME1Explorer.Unreal.Classes.TalkFile(pcc as ME1Package, indexTlk);
                                    //    locTlk.LoadTlkData();
                                    //}


                                    //2. Go through Entry list "m_EntryList"
                                    // Parse line TLK StrRef, TLK Line, Speaker -1 = Owner, -2 = Shepard, or from m_aSpeakerList

                                    var entryList = exp.GetProperty<ArrayProperty<StructProperty>>("m_EntryList");
                                    foreach (StructProperty entry in entryList)
                                    {
                                        //Get and set speaker name
                                        var speakeridx = entry.GetProp<IntProperty>("nSpeakerIndex");
                                        string lineSpeaker;
                                        if (speakeridx >= 0)
                                        {
                                            lineSpeaker = speakers[speakeridx];
                                        }
                                        else if (speakeridx == -2)
                                        {
                                            lineSpeaker = "Shepard";
                                        }
                                        else
                                        {
                                            lineSpeaker = "Owner";
                                        }

                                        //Get StringRef
                                        int lineStrRef = entry.GetProp<StringRefProperty>("srText").Value;
                                        if (lineStrRef > 0)
                                        {
                                            //Get StringRef Text
                                            string lineTLKstring = GlobalFindStrRefbyID(lineStrRef, GameBeingDumped, exp.FileRef as ME1Package);

                                            if (lineTLKstring != "No Data" && lineTLKstring != "\"\"" && lineTLKstring != "\" \"")
                                            {
                                                //Write to Background thread
                                                var excelout = new List<string> { "TLKStrings", lineSpeaker, lineStrRef.ToString(), lineTLKstring, convName, GameBeingDumped.ToString(), fileName, convIdx.ToString() };
                                                dumper._xlqueue.Add(excelout);
                                            }
                                        }
                                    }

                                    //3. Go through Reply list "m_ReplyList"
                                    // Parse line TLK StrRef, TLK Line, Speaker always Shepard
                                    var replyList = exp.GetProperty<ArrayProperty<StructProperty>>("m_ReplyList");
                                    if (replyList != null)
                                    {
                                        foreach (StructProperty reply in replyList)
                                        {
                                            //Get and set speaker name
                                            const string lineSpeaker = "Shepard";

                                            //Get StringRef
                                            var lineStrRef = reply.GetProp<StringRefProperty>("srText").Value;
                                            if (lineStrRef > 0)
                                            {

                                                //Get StringRef Text
                                                string lineTLKstring = GlobalFindStrRefbyID(lineStrRef, GameBeingDumped, exp.FileRef as ME1Package);
                                                if (lineTLKstring != "No Data" && lineTLKstring != "\"\"" && lineTLKstring != "\" \"")
                                                {
                                                    //Write to Background thread (must be 8 strings)
                                                    var excelout = new List<string> { "TLKStrings", lineSpeaker, lineStrRef.ToString(), lineTLKstring, convName, GameBeingDumped.ToString(), fileName, convIdx.ToString() };
                                                    dumper._xlqueue.Add(excelout);
                                                }
                                            }
                                        }
                                    }
                                }

                            }
                        }
                    }
                    //Build Table of conversation owner tags
                    if (CheckActor)
                    {
                        foreach (IExportEntry exp in pcc.Exports)
                        {
                            if (DumpCanceled)
                            {
                                return;
                            }

                            CurrentFileProgressValue = exp.UIndex;

                            string ownertag = "Not found";
                            className = exp.ClassName;
                            if (className == "BioSeqAct_StartConversation" || className == "SFXSeqAct_StartConversation" || className == "SFXSeqAct_StartAmbientConv")
                            {

                                string convo = "not found";  //Find Conversation 
                                var oconv = exp.GetProperty<ObjectProperty>("Conv");
                                if (oconv != null)
                                {
                                    int iconv = oconv.Value;
                                    if (iconv < 0)
                                    {
                                        convo = pcc.getUImport(iconv).ObjectName;
                                    }
                                    else
                                    {
                                        convo = pcc.getUExport(iconv).ObjectName;
                                    }
                                }

                                int iownerObj = 0; //Find owner tag in linked actor or variable
                                var links = exp.GetProperty<ArrayProperty<StructProperty>>("VariableLinks");
                                if (links != null)
                                {
                                    foreach (StructProperty l in links)
                                    {
                                        if (l.GetProp<StrProperty>("LinkDesc") == "Owner")
                                        {
                                            var ownerLink = l.GetProp<ArrayProperty<ObjectProperty>>("LinkedVariables");
                                            if (ownerLink.Any())
                                            {
                                                iownerObj = ownerLink[0].Value;
                                                break;
                                            }
                                        }
                                    }
                                }
                                if (iownerObj > 0)
                                {
                                    var svlink = pcc.getUExport(iownerObj);
                                    switch (svlink.ClassName)
                                    {
                                        case "SeqVar_Object":
                                        {
                                            ObjectProperty oactorlink = svlink.GetProperty<ObjectProperty>("ObjValue");
                                            if (oactorlink != null)
                                            {
                                                var actor = pcc.getUExport(oactorlink.Value);
                                                var actortag = actor.GetProperty<NameProperty>("Tag");
                                                if (actortag != null)
                                                {
                                                    ownertag = actortag.ToString();
                                                }
                                                else if (actor.idxArchtype != 0)
                                                {
                                                    var archtag = pcc.getUExport(actor.idxArchtype).GetProperty<NameProperty>("Tag");
                                                    if (archtag != null)
                                                    {
                                                        ownertag = archtag.ToString();
                                                    }
                                                }
                                            }
                                            break;
                                        }
                                        case "BioSeqVar_ObjectFindByTag" when GameBeingDumped == MEGame.ME3:
                                            ownertag = svlink.GetProperty<NameProperty>("m_sObjectTagToFind").ToString();
                                            break;
                                        case "BioSeqVar_ObjectFindByTag":
                                            ownertag = svlink.GetProperty<StrProperty>("m_sObjectTagToFind").ToString();
                                            break;
                                    }
                                }

                                //Write to Background thread 
                                var excelout = new List<string> { "ConvoOwners", convo, ownertag, fileName };
                                dumper._xlqueue.Add(excelout);

                            }

                            if (dumper.shouldDoDebugOutput)
                            {
                                string tag = null;
                                int strref = -1;
                                if(GameBeingDumped == MEGame.ME1 && className == "BioPawn")
                                {
                                    var tagprop = exp.GetProperty<NameProperty>("Tag");
                                    tag = tagprop.ToString();
                                    var behav = exp.GetProperty<ObjectProperty>("m_oBehavior");
                                    var set = pcc.getUExport(behav.Value).GetProperty<ObjectProperty>("m_oActorType");
                                    var strrefprop = pcc.getUExport(set.Value).GetProperty<StringRefProperty>("ActorGameNameStrRef");
                                    if (strrefprop != null)
                                    {
                                        strref = strrefprop.Value;
                                    }
                                }
                                else if(GameBeingDumped == MEGame.ME2 && className == "BioPawn")
                                {
                                    var tagprop = exp.GetProperty<NameProperty>("Tag");
                                    tag = tagprop.ToString();
                                    var type = exp.GetProperty<ObjectProperty>("ActorType");
                                    var strrefprop = pcc.getUExport(type.Value).GetProperty<StringRefProperty>("ActorGameNameStrRef");
                                    if (strrefprop != null)
                                    {
                                        strref = strrefprop.Value;
                                    }
                                }
                                else if(className == "SFXStuntActor" || className == "SFXPointOfInterest")
                                {

                                    var tagprop = exp.GetProperty<NameProperty>("Tag");
                                    tag = tagprop.Value;
                                    var modules = exp.GetProperty<ArrayProperty<ObjectProperty>>("Modules").ToList();
                                    var simplemod = modules.FirstOrDefault(m => exp.FileRef.getUExport(m.Value).ClassName == "SFXSimpleUseModule");
                                    strref = exp.FileRef.getUExport(simplemod.Value).GetProperty<StringRefProperty>("m_srGameName").Value;
                                }
                                else if (className.StartsWith("SFXPawn_"))
                                {
                                    try
                                    {
                                        var tagprop = exp.GetProperty<NameProperty>("Tag");
                                        tag = tagprop.Value;
                                        strref = exp.GetProperty<StringRefProperty>("PrettyName").Value;
                                    }
                                    catch
                                    {
                                        //ignore SFXPawns without prettyname don't add to Debug list
                                    }
                                }

                                if (tag != null && strref >= 0)
                                {
                                    string actorname = GlobalFindStrRefbyID(strref, GameBeingDumped, exp.FileRef as ME1Package);
                                    dumper._xlqueue.Add(new List<string> { "Tags", tag, strref.ToString(), actorname });
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                if (dumper.shouldDoDebugOutput)
                {
                    var excelout = new List<string> { "DEBUG", "FAILURE", fileName, className, CurrentFileProgressValue.ToString(), e.ToString() };
                    dumper._xlqueue.Add(excelout);
                }
            }
            
            if (dumper.shouldDoDebugOutput)
            {
                var excelout = new List<string> { "DEBUG", "SUCCESS", fileName};
                dumper._xlqueue.Add(excelout);
            }
        }
    }
}
