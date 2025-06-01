using System;

namespace WowAddonUpdater.Services
{
    public interface ITrayIconService : IDisposable
    {
        /// <summary>
        /// Event som triggas när användaren vill öppna huvudfönstret från tray
        /// </summary>
        event EventHandler ShowMainWindowRequested;

        /// <summary>
        /// Event som triggas när användaren vill stänga applikationen från tray
        /// </summary>
        event EventHandler ExitApplicationRequested;

        /// <summary>
        /// Visar eller döljer tray icon
        /// </summary>
        /// <param name="visible">True för att visa, false för att dölja</param>
        void SetVisible(bool visible);

        /// <summary>
        /// Visar ett balloon tip meddelande i tray
        /// </summary>
        /// <param name="title">Titel på meddelandet</param>
        /// <param name="text">Text i meddelandet</param>
        /// <param name="timeout">Hur länge meddelandet ska visas (millisekunder)</param>
        void ShowBalloonTip(string title, string text, int timeout = 3000);

        /// <summary>
        /// Uppdaterar tooltip text för tray icon
        /// </summary>
        /// <param name="text">Ny tooltip text</param>
        void SetTooltipText(string text);
    }
}