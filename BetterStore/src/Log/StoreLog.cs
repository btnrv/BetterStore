using System;
using System.IO;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;

namespace BetterStore.Log;

public static class StoreLog
{
    public enum LogType
    {
        GiveCurrency,
        GiftCurrency,
        Purchase,
        Equip,
        Sell
    }

    private static readonly object _writeLock = new();

    public static void SaveLog(string fromName, string fromSteamId, string toName, string toSteamId, int amount, LogType logType, ISwiftlyCore core)
    {
        _ = Task.Run(() =>
        {
            try
            {
                string logFolder = Path.Combine(core.CSGODirectory, "addons", "swiftlys2", "logs", "BetterStore");
                Directory.CreateDirectory(logFolder);

                string logFile = Path.Combine(logFolder, $"{DateTime.Now:dd.MM.yyyy}-{logType.ToString().ToLower()}.log");

                string line = $"[{DateTime.Now:HH:mm:ss}] From: {fromName} ({fromSteamId}) -> To: {toName} ({toSteamId}) | Amount: {amount}";

                lock (_writeLock)
                {
                    File.AppendAllText(logFile, line + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                core.Logger.LogError(ex, "BetterStore log error");
            }
        });
    }
}
