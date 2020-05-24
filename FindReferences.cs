#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

/// <summary>Not Support mac yet.</summary>
public static class FindReferences
{
    private const String _menuItemName = "Assets/Find References In Project #&f";
    private const String _metaExtension = ".meta";

    private static readonly Int32 _platform =
        Application.platform == RuntimePlatform.WindowsEditor ? 0 : 1;

    private static readonly String _dataPath = Application.dataPath;
    private static readonly String[] _program =
        {
            $"{_dataPath}/Scripts/Tools/.rg-win.exe",   // win
            $"{_dataPath}/Scripts/Tools/.rg-mac"        // mac
        };
    private static readonly String[] _preprocessor =
        {
            $"{_dataPath}/Scripts/Tools/.rgxxdwin.bat", // win
            $"{_dataPath}/Scripts/Tools/.rgxxdmac"      // mac
        };

    /// <summary>0 means text search, 1 means binary search.</summary>
    private static Int32 _searchMode = 1;
    private static readonly String[] _arguments =
        {
            String.Join(" ",
                "--files-with-matches --fixed-strings --follow",
                $"--ignore-file {_dataPath}/Scripts/Tools/.rgIgnore",
                "--regexp {0}",
                $"--threads {Environment.ProcessorCount}",
                $"-- {_dataPath}"
            ),
            String.Join(" ",
                "--files-with-matches --fixed-strings --follow",
                $"--ignore-file {_dataPath}/Scripts/Tools/.rgIgnore",
                "--regexp {0}",
                $"--pre {_preprocessor[_platform]}",
                $"--threads {Environment.ProcessorCount}",
                $"-- {_dataPath}"
            )
        };

    private static readonly Double _waitSeconds = _searchMode == 0 ? 60 : 600;

    [MenuItem(_menuItemName, true)]
    private static Boolean FindValidate()
    {
        UnityEngine.Object obj = Selection.activeObject;

        if (obj != null && AssetDatabase.Contains(obj))
            return !AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(obj));

        return false;
    }

    [MenuItem(_menuItemName, false, 25)]
    public static void Find()
    {
        UnityEngine.Object obj = Selection.activeObject;

        String path = AssetDatabase.GetAssetPath(obj);
        String guid = AssetDatabase.AssetPathToGUID(path);
        String metaPath = path + _metaExtension;

        List<String> references = new List<String>();
        StringBuilder errors = new StringBuilder();

        Double totalTime = RunProcess();
        Output();

        // ---------- Local Functions

        Double RunProcess()
        {
            Process process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    Arguments = String.Format(_arguments[_searchMode], guid),
                    CreateNoWindow = true,
                    FileName = _program[_platform],
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false     // needed to be false to redirect io streams
                }
            };
            process.OutputDataReceived += OutputDataReceived;
            process.ErrorDataReceived += ErrorDataReceived;

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            while (process.HasExited == false)
            {
                if (stopwatch.ElapsedMilliseconds >= _waitSeconds * 1000)
                {
                    process.Kill();
                    break;
                }
                else
                {
                    Double elapsed = stopwatch.ElapsedMilliseconds / 1000d;
                    Double progress = elapsed / _waitSeconds;

                    Boolean canceled = EditorUtility.DisplayCancelableProgressBar(
                        "Find References in Project",
                        $"Finding {elapsed}/{_waitSeconds}s {progress:P2}",
                        (Single)progress
                    );

                    if (canceled)
                    {
                        process.Kill();
                        break;
                    }

                    Thread.Sleep(100);
                }
            }

            EditorUtility.ClearProgressBar();
            stopwatch.Stop();

            return stopwatch.ElapsedMilliseconds / 1000d;
        }

        void OutputDataReceived(System.Object sender, DataReceivedEventArgs eventArgs)
        {
            if (String.IsNullOrEmpty(eventArgs.Data))
                return;

            String relativeReference = eventArgs.Data.Replace(_dataPath, "Assets");

            /*
            * Skip the meta file or whatever we selected.
            */
            if (relativeReference == metaPath)
                return;

            references.Add(relativeReference);
        }

        void ErrorDataReceived(System.Object sender, DataReceivedEventArgs eventArgs)
        {
            if (String.IsNullOrEmpty(eventArgs.Data) == false)
                errors.AppendLine($"Error: {eventArgs.Data}");
        }

        void Output()
        {
            foreach (String reference in references)
            {
                String refGuid = AssetDatabase.AssetPathToGUID(reference);

                String file = reference;

                if (reference.EndsWith(_metaExtension))
                    file = reference.Substring(0, reference.Length - _metaExtension.Length);

                UnityEngine.Debug.Log(reference, AssetDatabase.LoadMainAssetAtPath(file));
            }

            Int32 count = references.Count;
            String log = $"Cost {totalTime}s, {count} reference{(count > 2 ? "s" : "")} found for";
            log += $" \"{obj.name}\" [{path}, {guid}]";

            UnityEngine.Debug.Log($"<b><color=#FF5522>{log}</color></b>", obj);

            String errorLog = errors.ToString();
            if (String.IsNullOrEmpty(errorLog) == false)
                UnityEngine.Debug.Log($"Errors when finding references:\n{errorLog}");
        }
    }
}

#endif
