using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Web.Mvc;
using AForge.Video;
using AForge.Video.DirectShow;
using Oracle.ManagedDataAccess.Client;
using System.Timers;

namespace WebcamCapture.Controllers
{
    public class HomeController : Controller
    {
        private const string ConnectionString = "User Id=TEST; Password=password1; Data Source=localhost:1521/iot";
        private static bool isCapturing = false;
        private static VideoCaptureDevice[] videoSources;
        private static Timer captureTimer;
        private static Bitmap[] latestFrames;
        private static FilterInfoCollection videoDevices;
        private static List<string> logs = new List<string>();

        public ActionResult Index()
        {
            videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            ViewBag.Cameras = videoDevices;
            ViewBag.Logs = logs;
            return View();
        }

        [HttpPost]
        public ActionResult StartCapture(int interval)
        {
            string directory = Server.MapPath("~/CaptureImage");

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var cameraIndices = DetectConnectedCameras();

            if (cameraIndices.Count == 0)
            {
                ViewBag.Message = "No cameras detected!";
                return View("Index");
            }

            if (isCapturing)
            {
                ViewBag.Message = "Capture already in progress!";
                return View("Index");
            }

            isCapturing = true;
            videoSources = new VideoCaptureDevice[cameraIndices.Count];
            latestFrames = new Bitmap[cameraIndices.Count];

            for (int i = 0; i < cameraIndices.Count; i++)
            {
                int cameraIndex = cameraIndices[i];
                videoSources[i] = new VideoCaptureDevice(videoDevices[cameraIndex].MonikerString);
                int index = i;
                videoSources[i].NewFrame += (sender, eventArgs) =>
                {
                    latestFrames[index] = (Bitmap)eventArgs.Frame.Clone();
                };
                videoSources[i].Start();
            }

            captureTimer = new Timer(interval * 1000);
            captureTimer.Elapsed += (sender, e) => CaptureFrames(directory);
            captureTimer.Start();

            logs.Add("Capture started!");
            ViewBag.Message = "Capture started!";
            ViewBag.Logs = logs;
            return View("Index");
        }

        public ActionResult StopCapture()
        {
            if (!isCapturing)
            {
                ViewBag.Message = "No capture in progress!";
                ViewBag.Logs = logs;
                return View("Index");
            }

            isCapturing = false;
            captureTimer.Stop();
            captureTimer.Dispose();

            foreach (var videoSource in videoSources)
            {
                if (videoSource.IsRunning)
                {
                    videoSource.SignalToStop();
                    videoSource.WaitForStop();
                }
            }

            logs.Add("Capture stopped!");
            ViewBag.Message = "Capture stopped!";
            ViewBag.Logs = logs;
            return View("Index");
        }

        private List<int> DetectConnectedCameras()
        {
            var cameraIndices = new List<int>();

            for (int i = 0; i < videoDevices.Count; i++)
            {
                cameraIndices.Add(i);
            }

            return cameraIndices;
        }

        private void CaptureFrames(string directory)
        {
            for (int i = 0; i < videoSources.Length; i++)
            {
                if (latestFrames[i] != null)
                {
                    string dateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    string sourceText = $"Source: {videoDevices[i].Name}";
                    using (Graphics g = Graphics.FromImage(latestFrames[i]))
                    {
                        float yOffset = 40;
                        float xDateTime = 10;
                        float yDateTime = latestFrames[i].Height - yOffset - 20;
                        float xSource = 10;
                        float ySource = latestFrames[i].Height - yOffset;

                        AddTextWithOutline(g, dateTime, new PointF(xDateTime, yDateTime));
                        AddTextWithOutline(g, sourceText, new PointF(xSource, ySource));
                    }

                    string fileName = $"CAM_{videoDevices[i].Name}_{DateTime.Now:yyyyMMdd_HHmmss}.jpg";
                    string path = Path.Combine(directory, fileName);
                    latestFrames[i].Save(path, System.Drawing.Imaging.ImageFormat.Jpeg);

                    InsertCaptureData(dateTime, $"{videoDevices[i].Name}", fileName, path);
                    logs.Add($"Frame captured: {fileName}");
                }
            }
        }

        private void AddTextWithOutline(Graphics g, string text, PointF point)
        {
            Font font = new Font("Arial", 12);
            Brush brush = Brushes.White;
            Brush outlineBrush = Brushes.Black;
            PointF outlinePoint1 = new PointF(point.X - 1, point.Y - 1);
            PointF outlinePoint2 = new PointF(point.X + 1, point.Y - 1);
            PointF outlinePoint3 = new PointF(point.X - 1, point.Y + 1);
            PointF outlinePoint4 = new PointF(point.X + 1, point.Y + 1);

            g.DrawString(text, font, outlineBrush, outlinePoint1);
            g.DrawString(text, font, outlineBrush, outlinePoint2);
            g.DrawString(text, font, outlineBrush, outlinePoint3);
            g.DrawString(text, font, outlineBrush, outlinePoint4);
            g.DrawString(text, font, brush, point);
        }

        private void InsertCaptureData(string dateTime, string sourceAsset, string fileName, string fileLoc)
        {
            using (var connection = new OracleConnection(ConnectionString))
            {
                try
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "INSERT INTO CAPTURE (DATETIME, SOURCEASSET, FILENAME, FILELOC) VALUES (:dateTime, :sourceAsset, :fileName, :fileLoc)";

                        DateTime parsedDateTime;
                        if (DateTime.TryParse(dateTime, out parsedDateTime))
                        {
                            command.Parameters.Add("dateTime", OracleDbType.TimeStamp).Value = parsedDateTime;
                        }
                        else
                        {
                            logs.Add($"Failed to parse datetime: {dateTime}");
                            return;
                        }

                        command.Parameters.Add("sourceAsset", OracleDbType.Varchar2).Value = sourceAsset;
                        command.Parameters.Add("fileName", OracleDbType.Varchar2).Value = fileName;
                        command.Parameters.Add("fileLoc", OracleDbType.Varchar2).Value = fileLoc;

                        command.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    logs.Add($"Error inserting capture data: {ex.Message}");
                }
            }
        }
    }
}
