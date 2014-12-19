﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TuesPechkin.Wkhtmltox;

namespace TuesPechkin.Tests
{
    [TestClass]
    public class PechkinTests
    {
        private const string TEST_WK_VER = "0.12.1";
        private const string TEST_URL = "www.google.com";

        static PechkinTests()
        {
            Debug.Listeners.Add(new DefaultTraceListener());
        }

        public static string GetResourceString(string name)
        {
            if (name == null)
            {
                return null;
            }

            Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);

            if (s == null)
            {
                return null;
            }

            return new StreamReader(s).ReadToEnd();
        }

        [TestMethod]
        public void BubblesExceptionsFromSyncedThread()
        {
            var converter = new ThreadSafeConverter(new BogusToolset());

            try
            {
                converter.Convert(Document(StringObject()));
                Assert.Fail();
            }
            catch (NotImplementedException) { }
        }

        [TestMethod]
        public void ConvertsAfterAppDomainRecycles()
        {
            // arrange
            var domain1 = this.GetAppDomain("testing_unload_1");
            byte[] result1 = null;
            var domain2 = this.GetAppDomain("testing_unload_2");
            byte[] result2 = null;
            CrossAppDomainDelegate callback = () =>
            {
                var dllPath = AppDomain.CurrentDomain.GetData("dllpath") as string;

                var converter =
                    new ThreadSafeConverter(
                        new PdfToolset(
                            new StaticDeployment(dllPath)));

                var document = new HtmlDocument("<p>some html</p>");

                AppDomain.CurrentDomain.SetData("result", converter.Convert(document));
            };

            // act
            domain1.SetData("dllpath", GetDeploymentPath());
            domain1.DoCallBack(callback);
            result1 = domain1.GetData("result") as byte[];
            AppDomain.Unload(domain1);

            domain2.SetData("dllpath", GetDeploymentPath());
            domain2.DoCallBack(callback);
            result2 = domain2.GetData("result") as byte[];
            AppDomain.Unload(domain2);

            // assert
            Assert.IsNotNull(result1);
            Assert.IsNotNull(result2);
        }

        [TestMethod]
        public void HandlesConcurrentThreads()
        {
            int numberOfTasks = 10;
            int completed = 0;

            IConverter converter = new ThreadSafeConverter(GetNewToolset());

            var tasks = Enumerable.Range(0, numberOfTasks).Select(i => new Task(() =>
            {
                Debug.WriteLine(String.Format("#{0} started", i + 1));
                Assert.IsNotNull(converter.Convert(Document(StringObject())));
                completed++;
                Debug.WriteLine(String.Format("#{0} completed", i + 1));
            }));

            Parallel.ForEach(tasks, task => task.Start());

            while (completed < numberOfTasks)
            {
                Thread.Sleep(1000);
            }
        }

        [TestMethod]
        public void TwoSequentialConversionsFromString()
        {
            var converter = GetNewConverter();
            byte[] result = null;

            result = converter.Convert(Document(StringObject()));

            Assert.IsNotNull(result);

            result = converter.Convert(Document(StringObject()));

            Assert.IsNotNull(result);
        }

        [TestMethod]
        public void MultipleObjectConversionFromString()
        {
            var converter = GetNewConverter();
            byte[] result = null;

            result = converter.Convert(Document(StringObject(), StringObject()));

            Assert.IsNotNull(result);
        }

        [TestMethod]
        public void TwoSequentialConversionsFromUrl()
        {
            var converter = GetNewConverter();
            byte[] result = null;

            result = converter.Convert(Document(UrlObject()));

            Assert.IsNotNull(result);

            result = converter.Convert(Document(UrlObject()));

            Assert.IsNotNull(result);
        }

        [TestMethod]
        public void MultipleObjectConversionFromUrl()
        {
            var converter = GetNewConverter();
            byte[] result = null;

            result = converter.Convert(Document(UrlObject(), UrlObject()));

            Assert.IsNotNull(result);
        }

        [TestMethod]
        public void ResultIsPdf()
        {
            string html = GetResourceString("PechkinTests.Resources.page.html");

            IConverter c = GetNewConverter();

            byte[] ret = c.Convert(Document(StringObject()));

            Assert.IsNotNull(ret);

            byte[] right = Encoding.UTF8.GetBytes("%PDF");

            Assert.IsTrue(right.Length <= ret.Length);

            byte[] test = new byte[right.Length];
            Array.Copy(ret, 0, test, 0, right.Length);

            for (int i = 0; i < right.Length; i++)
            {
                Assert.AreEqual(right[i], test[i]);
            }
        }

        [TestMethod]
        public void ReturnsResultFromFile()
        {
            string html = GetResourceString("PechkinTests.Resources.page.html");

            string fn = string.Format("{0}.html", Path.GetTempFileName());
            FileStream fs = new FileStream(fn, FileMode.Create);
            StreamWriter sw = new StreamWriter(fs);

            sw.Write(html);

            sw.Close();

            var c = GetNewConverter();

            byte[] ret = c.Convert(new HtmlDocument
            {
                Objects = { 
                    new ObjectSettings { PageUrl = fn } 
                }
            });

            Assert.IsNotNull(ret);

            File.Delete(fn);
        }

        [TestMethod]
        public void ReturnsResultFromString()
        {
            var converter = GetNewConverter();

            var document = new HtmlDocument("<p>some html</p>");

            var result = converter.Convert(document);

            Assert.IsNotNull(result);
        }

        [TestMethod]
        public void UnloadsWkhtmltoxWhenAppDomainUnloads()
        {
            // arrange
            var domain = GetAppDomain("testing_unload");

            // act
            domain.DoCallBack(() =>
            {
                var converter =
                    new StandardConverter(
                        new RemotingToolset<PdfToolset>(
                            new WinEmbeddedDeployment(
                                new StaticDeployment(Path.GetTempPath()))));

                var document = new HtmlDocument("<p>some html</p>");

                converter.Convert(document);
            });
            AppDomain.Unload(domain);

            // assert
            Assert.IsFalse(Process
                .GetCurrentProcess()
                .Modules
                .Cast<ProcessModule>()
                .Any(m => m.ModuleName == "wkhtmltox.dll"));
        }

        private AppDomain GetAppDomain(string name)
        {
            return AppDomain.CreateDomain(name, null, AppDomain.CurrentDomain.SetupInformation);
        }

        private string GetDeploymentPath()
        {
            return Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "wk-ver",
                TEST_WK_VER);
        }

        private IConverter GetNewConverter()
        {
            return new StandardConverter(GetNewToolset());
        }

        private IToolset GetNewToolset()
        {
            return new PdfToolset(GetNewDeployment());
        }

        private IDeployment GetNewDeployment()
        {
            return new StaticDeployment(GetDeploymentPath());
        }

        private HtmlDocument Document(params ObjectSettings[] objects)
        {
            var doc = new HtmlDocument();
            doc.Objects.AddRange(objects);

            return doc;
        }

        private ObjectSettings StringObject()
        {
            var html = GetResourceString("PechkinTests.Resources.page.html");
            
            return new ObjectSettings { HtmlText = html };
        }

        private ObjectSettings UrlObject()
        {
            return new ObjectSettings { PageUrl = TEST_URL };
        }
    }
}