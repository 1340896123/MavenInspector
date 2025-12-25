
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace MavenInspector 
{
    public class WinCMDHelper
    {
        private WinCMDHelper()
        { }

        private static WinCMDHelper _cmd = null;

        public static WinCMDHelper GetInstance()
        {
            return _cmd ?? (_cmd = new WinCMDHelper());
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        public string RunFfmpeg(string args)
        {
            if (string.IsNullOrEmpty(args))
            {
                return "Err:输入参数不正确!";
            }
            string cmd = "";// vHelper.FFMpeg;

            return RunCmd(cmd, args); ;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        public string RunCmd(string cmd, string args)
        {
            bool isShowMsg = true;
            Process myProcess = new Process();
            //using (Process myProcess = new Process()) //创建进程对象  
            {
                //准备读出输出流和错误流
                string outputData = string.Empty;
                string errorData = string.Empty;
                try
                {
                    myProcess.StartInfo.UseShellExecute = false; //是否使用系统外壳程序启动  
                    myProcess.StartInfo.FileName = cmd;          //设定需要执行的命令
                    myProcess.StartInfo.CreateNoWindow = !isShowMsg;  //是否创建窗口 
                    myProcess.StartInfo.WindowStyle = isShowMsg ? System.Diagnostics.ProcessWindowStyle.Normal : System.Diagnostics.ProcessWindowStyle.Hidden;
                    myProcess.StartInfo.Arguments = args;
                    myProcess.StartInfo.RedirectStandardInput = false;   //是否重定向输入
                    myProcess.StartInfo.RedirectStandardError = !isShowMsg;    //重定向错误输出
                    //myProcess.StartInfo.RedirectStandardOutput = true; //重定向输出
                    //myProcess.EnableRaisingEvents = true;
                    //myProcess.Exited += myProcess_Exited;
                    myProcess.Start();

                    //StreamReader errorreader = myProcess.StandardError;
                    //myProcess.OutputDataReceived += (ss, ee) =>
                    //{
                    //    outputData += ee.Data;
                    //};
                    //myProcess.ErrorDataReceived += (ss, ee) =>
                    //{
                    //    errorData += ee.Data;
                    //};

                    myProcess.WaitForExit();
                    string result = "";
                    //string result = errorreader.ReadToEnd();
                    //string output = myProcess.StandardOutput.ReadToEnd();//读取进程的输出  

                    return string.IsNullOrEmpty(result) ? "" : "Err:" + result;
                }
                catch (Exception ex)
                {
                    Logger.Log($"WinCMDHelper.RunCmd Error: {ex.Message}", "ERROR");
                    return ex.Message;
                }
            }
        }

        public string RunDosCmd(string cmd, string args)
        {
            string dosCmd = cmd + args;
            Process myProcess = new Process(); //创建进程对象  
            try
            {
                myProcess.StartInfo.UseShellExecute = false; //是否使用系统外壳程序启动  
                myProcess.StartInfo.FileName = "cmd.exe"; //设定需要执行的命令
                myProcess.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                myProcess.StartInfo.CreateNoWindow = true; //是否创建窗口 
                myProcess.StartInfo.Arguments = "";
                myProcess.StartInfo.RedirectStandardInput = true;  //是否重定向输入
                myProcess.StartInfo.RedirectStandardError = true;   //重定向输出
                myProcess.StartInfo.RedirectStandardOutput = true; //重定向输出
                //myProcess.EnableRaisingEvents = true;
                //myProcess.Exited += myProcess_Exited;
                myProcess.Start();

                myProcess.StandardInput.WriteLine(dosCmd);

                StreamReader errorreader = myProcess.StandardError;

                myProcess.StandardInput.WriteLine("exit");

                myProcess.WaitForExit();

                string result = errorreader.ReadToEnd();
                string output = myProcess.StandardOutput.ReadToEnd();//读取进程的输出  

                return string.IsNullOrEmpty(result) ? output : "Err:" + result;
            }
            catch (Exception ex)
            {
                Logger.Log($"WinCMDHelper.RunDosCmd Error: {ex.Message}", "ERROR");
                return ex.Message;
            }
        }

        public string RunPowerShell(string scriptText)
        {
            using (Process myProcess = new Process())
            {
                try
                {
                    myProcess.StartInfo.UseShellExecute = false;
                    myProcess.StartInfo.FileName = "powershell.exe";
                    myProcess.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                    myProcess.StartInfo.CreateNoWindow = true;
                    // -Command - tells PowerShell to read the command from StandardInput
                    myProcess.StartInfo.Arguments = "-NoLogo -NoProfile -ExecutionPolicy Bypass -Command -";
                    myProcess.StartInfo.RedirectStandardInput = true;
                    myProcess.StartInfo.RedirectStandardError = true;
                    myProcess.StartInfo.RedirectStandardOutput = true;

                    myProcess.Start();

                    // Write command to stdin
                    myProcess.StandardInput.WriteLine(scriptText);
                    myProcess.StandardInput.WriteLine("exit");
                    myProcess.StandardInput.Close(); // Close input to signal end of commands

                    // Read output and error asynchronously to avoid deadlocks
                    // (If we wait for exit before reading, and the buffer fills up, it deadlocks)
                    var outputTask = myProcess.StandardOutput.ReadToEndAsync();
                    var errorTask = myProcess.StandardError.ReadToEndAsync();

                    // Wait for the process to exit
                    myProcess.WaitForExit();

                    // Ensure we have all the output
                    Task.WaitAll(outputTask, errorTask);

                    string output = outputTask.Result;
                    string result = errorTask.Result;

                    if (!string.IsNullOrEmpty(result))
                    {
                        Logger.Log($"PowerShell Error Output: {result}", "WARN");
                    }

                    return string.IsNullOrEmpty(result) ? output : "Err:" + result;
                }
                catch (Exception ex)
                {
                    Logger.Log($"WinCMDHelper.RunPowerShell Error: {ex.Message}", "ERROR");
                    return ex.Message;
                }
            }
        }

        void myProcess_Exited(object sender, EventArgs e)
        {
            //throw new NotImplementedException();
        }
    }
}