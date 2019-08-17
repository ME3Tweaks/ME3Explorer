﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ME3Explorer.Packages
{
    public abstract class UnrealPackageFile : NotifyPropertyChangedBase
    {
        protected const uint packageTag = 0x9E2A83C1;
        public string FilePath { get; protected set; }

        public bool IsModified
        {
            get
            {
                return exports.Any(entry => entry.DataChanged || entry.HeaderChanged) || imports.Any(entry => entry.HeaderChanged) || namesAdded > 0;
            }
        }
        public abstract int NameCount { get; protected set; }
        public abstract int ExportCount { get; protected set; }
        public abstract int ImportCount { get; protected set; }

        #region Names
        protected uint namesAdded;
        protected List<string> names;
        public IReadOnlyList<string> Names => names;

        public bool isName(int index) => index >= 0 && index < names.Count;

        public string getNameEntry(int index) => isName(index) ? names[index] : "";

        public int FindNameOrAdd(string name)
        {
            for (int i = 0; i < names.Count; i++)
            {
                if (names[i] == name)
                    return i;
            }

            addName(name);
            return names.Count - 1;
        }

        public void addName(string name)
        {
            if (name == null)
            {
                throw new Exception("Cannot add a null name to the list of names for a package file.\nThis is a bug in ME3Explorer.");
            }
            if (!names.Contains(name))
            {
                names.Add(name);
                namesAdded++;
                NameCount = names.Count;

                updateTools(PackageChange.Names, NameCount - 1);
                OnPropertyChanged(nameof(NameCount));
            }
        }

        public void replaceName(int idx, string newName)
        {
            if (isName(idx))
            {
                names[idx] = newName;
                updateTools(PackageChange.Names, idx);
            }
        }

        /// <summary>
        /// Checks whether a name exists in the PCC and returns its index
        /// If it doesn't exist returns -1
        /// </summary>
        /// <param name="nameToFind">The name of the string to find</param>
        /// <returns></returns>
        public int findName(string nameToFind)
        {
            for (int i = 0; i < names.Count; i++)
            {
                if (string.Compare(nameToFind, getNameEntry(i)) == 0)
                    return i;
            }
            return -1;
        }

        public void setNames(List<string> list) => names = list;

        #endregion

        #region Exports
        protected List<ExportEntry> exports;
        public IReadOnlyList<ExportEntry> Exports => exports;

        public bool isUExport(int uindex) => uindex > 0 && uindex <= exports.Count;

        public void addExport(ExportEntry exportEntry)
        {
            if (exportEntry.FileRef != this)
                throw new Exception("Cannot add an export entry from another package file");

            exportEntry.DataChanged = true;
            exportEntry.Index = exports.Count;
            exportEntry.PropertyChanged += exportChanged;
            exports.Add(exportEntry);
            ExportCount = exports.Count;

            updateTools(PackageChange.ExportAdd, ExportCount - 1);
            OnPropertyChanged(nameof(ExportCount));
        }

        public ExportEntry getExport(int index) => exports[index];
        public ExportEntry getUExport(int uindex) => exports[uindex - 1];

        #endregion

        #region Imports
        protected List<ImportEntry> imports;
        public IReadOnlyList<ImportEntry> Imports => imports;

        public bool isUImport(int uindex) => (uindex < 0 && Math.Abs(uindex) <= ImportCount);

        public void addImport(ImportEntry importEntry)
        {
            if (importEntry.FileRef != this)
                throw new Exception("you cannot add a new import entry from another package file, it has invalid references!");

            importEntry.Index = imports.Count;
            importEntry.PropertyChanged += importChanged;
            imports.Add(importEntry);
            importEntry.EntryHasPendingChanges = true;
            ImportCount = imports.Count;

            updateTools(PackageChange.ImportAdd, ImportCount - 1);
            OnPropertyChanged(nameof(ImportCount));
        }

        public ImportEntry getUImport(int uindex) => imports[Math.Abs(uindex) - 1];

        #endregion

        #region IEntry
        /// <summary>
        ///     gets Export or Import name
        /// </summary>
        /// <param name="uIndex">unreal index</param>
        public string getObjectName(int uIndex)
        {
            if (isEntry(uIndex))
                return getEntry(uIndex).ObjectName;
            if (uIndex == 0)
                return "Class";
            return "";
        }

        /// <summary>
        ///     gets Export or Import class
        /// </summary>
        /// <param name="uIndex">unreal index</param>
        public string getObjectClass(int uIndex)
        {
            if (isEntry(uIndex))
                return getEntry(uIndex).ClassName;
            return "";
        }

        /// <summary>
        ///     gets Export or Import entry
        /// </summary>
        /// <param name="uindex">unreal index</param>
        public IEntry getEntry(int uindex)
        {
            if (isUExport(uindex))
                return exports[uindex - 1];
            if (isUImport(uindex))
                return imports[-uindex - 1];
            return null;
        }
        public bool isEntry(int uindex) => (uindex > 0 && uindex <= ExportCount) || (uindex < 0 && -uindex <= ImportCount);

        #endregion

        private DateTime? lastSaved;
        public DateTime LastSaved
        {
            get
            {
                if (lastSaved.HasValue)
                {
                    return lastSaved.Value;
                }

                if (File.Exists(FilePath))
                {
                    return (new FileInfo(FilePath)).LastWriteTime;
                }

                return DateTime.MinValue;
            }
        }

        public long FileSize => File.Exists(FilePath) ? (new FileInfo(FilePath)).Length : 0;

        protected virtual void AfterSave()
        {
            //We do if checks here to prevent firing tons of extra events as we can't prevent firing change notifications if 
            //it's not really a change due to the side effects of suppressing that.
            foreach (var export in exports)
            {
                if (export.DataChanged)
                {
                    export.DataChanged = false;
                }
                if (export.HeaderChanged)
                {
                    export.HeaderChanged = false;
                }
                if (export.EntryHasPendingChanges)
                {
                    export.EntryHasPendingChanges = false;
                }
            }
            foreach (var import in imports)
            {
                if (import.HeaderChanged)
                {
                    import.HeaderChanged = false;
                }
                if (import.EntryHasPendingChanges)
                {
                    import.EntryHasPendingChanges = false;
                }
            }
            namesAdded = 0;

            lastSaved = DateTime.Now;
            OnPropertyChanged(nameof(LastSaved));
            OnPropertyChanged(nameof(FileSize));
            OnPropertyChanged(nameof(IsModified));
        }

        #region packageHandler stuff
        public ObservableCollection<GenericWindow> Tools { get; } = new ObservableCollection<GenericWindow>();

        public void RegisterTool(GenericWindow gen)
        {
            RefCount++;
            Tools.Add(gen);
            gen.RegisterClosed(() =>
            {
                ReleaseGenericWindow(gen);
                Dispose();
            });
        }

        public void Release(System.Windows.Window wpfWindow = null, System.Windows.Forms.Form winForm = null)
        {
            if (wpfWindow != null)
            {
                GenericWindow gen = Tools.FirstOrDefault(x => x == wpfWindow);
                if (gen is GenericWindow) //can't use != due to ambiguity apparently
                {
                    ReleaseGenericWindow(gen);
                }
                else
                {
                    Debug.WriteLine("Releasing package that isn't in use by any window");
                }
            }
            else if (winForm != null)
            {
                GenericWindow gen = Tools.FirstOrDefault(x => x == winForm);
                if (gen is GenericWindow) //can't use != due to ambiguity apparently
                {
                    ReleaseGenericWindow(gen);
                }
                else
                {
                    Debug.WriteLine("Releasing package that isn't in use by any window");
                }
            }
            Dispose();
        }

        private void ReleaseGenericWindow(GenericWindow gen)
        {
            Tools.Remove(gen);
            if (Tools.Count == 0)
            {
                noLongerOpenInTools?.Invoke(this);
            }
            gen.Dispose();
        }

        public delegate void MEPackageEventHandler(UnrealPackageFile sender);
        public event MEPackageEventHandler noLongerOpenInTools;

        protected void exportChanged(object sender, PropertyChangedEventArgs e)
        {
            if (sender is ExportEntry exp)
            {
                switch (e.PropertyName)
                {
                    case nameof(ExportEntry.DataChanged):
                        updateTools(PackageChange.ExportData, exp.Index);
                        break;
                    case nameof(ExportEntry.HeaderChanged):
                        updateTools(PackageChange.ExportHeader, exp.Index);
                        break;
                }
            }
        }

        protected void importChanged(object sender, PropertyChangedEventArgs e)
        {
            if (sender is ImportEntry imp
             && e.PropertyName == nameof(ImportEntry.HeaderChanged))
            {
                updateTools(PackageChange.Import, imp.Index);
            }
        }

        readonly HashSet<PackageUpdate> pendingUpdates = new HashSet<PackageUpdate>();
        readonly List<Task> tasks = new List<Task>();
        readonly Dictionary<int, bool> taskCompletion = new Dictionary<int, bool>();
        const int queuingDelay = 50;
        protected void updateTools(PackageChange change, int index)
        {
            PackageUpdate update = new PackageUpdate { change = change, index = index };
            if (!pendingUpdates.Contains(update))
            {
                pendingUpdates.Add(update);
                Task task = Task.Delay(queuingDelay);
                taskCompletion[task.Id] = false;
                tasks.Add(task);
                task.ContinueWithOnUIThread(x =>
                {
                    taskCompletion[x.Id] = true;
                    if (tasks.TrueForAll(t => taskCompletion[t.Id]))
                    {
                        tasks.Clear();
                        taskCompletion.Clear();
                        foreach (var item in Tools)
                        {
                            item.handleUpdate(pendingUpdates.ToList());
                        }
                        pendingUpdates.Clear();
                        OnPropertyChanged(nameof(IsModified));
                    }
                });
            }
        }

        public event MEPackageEventHandler noLongerUsed;
        private int RefCount;

        public void RegisterUse() => RefCount++;

        /// <summary>
        /// Doesn't neccesarily dispose the object.
        /// Will only do so once this has been called by every place that uses it.
        /// HIGHLY Recommend using the using block instead of calling this directly.
        /// </summary>
        public void Dispose()
        {
            RefCount--;
            if (RefCount == 0)
            {
                noLongerUsed?.Invoke(this);
            }
        }
        #endregion
    }
}