#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

public static class FindReferences
{
    private const String _menuItemName = "Assets/Find References In Project #&f";
    private const String _metaExtension = ".meta";

    private static readonly String _dataPath = Application.dataPath;

    private static readonly Boolean _isOSX = Application.platform == RuntimePlatform.OSXEditor;
    private static readonly Double _waitSeconds = _isOSX ? 2 : 300;

    private static readonly String _program =
        _isOSX ? "/usr/bin/mdfind" : $"{Environment.CurrentDirectory}\\Tools\\rg.exe";

    private static readonly String _arguments =
        _isOSX ? $"-onlyin {_dataPath} {{0}}"
            : String.Join(" ",
                "--case-sensitive",
                "--files-with-matches",
                "--fixed-strings",
                "--follow",
                $"--ignore-file Assets\\Tools\\Unity.FindReferences\\.rgIgnore",
                "--no-text",
                "--regexp {0}",
                $"--threads {Environment.ProcessorCount}",
                $"-- {_dataPath}"
            );

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
        StringBuilder output = new StringBuilder();

        Double totalTime = RunProcess();
        Output();

        // ---------- Local Functions

        Double RunProcess()
        {
            Process process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    Arguments = String.Format(_arguments, guid),
                    CreateNoWindow = true,
                    FileName = _program,
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
            if (String.IsNullOrEmpty(eventArgs.Data))
                return;

            output.AppendLine($"Error: {eventArgs.Data}");
        }

        void Output()
        {
            foreach (String reference in references)
            {
                String refGuid = AssetDatabase.AssetPathToGUID(reference);
                output.AppendLine($"{refGuid}: {reference}");

                String file = reference;

                if (reference.EndsWith(_metaExtension))
                    file = reference.Substring(0, reference.Length - _metaExtension.Length);

                UnityEngine.Debug.Log(
                    $"{refGuid}: {reference}", AssetDatabase.LoadMainAssetAtPath(file)
                );
            }

            String log = $"<b><color=#FF5522>Cost {totalTime}s,";
            log += $" {references.Count} reference{(references.Count > 2 ? "s" : "")} found for";
            log += $" object: \"{obj.name}\" path: \"{path}\" guid: \"{guid}\"</color></b>\n";

            UnityEngine.Debug.Log(log + output, obj);
        }
    }
}

#endif
