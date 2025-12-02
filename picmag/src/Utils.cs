// MIT License
//
// Copyright (c) 2025 Dimitri Ratz
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.IO;

namespace picmag
{
    public class Utils
    {
        public byte[] GetMd5(string filePath)
        {
            byte[] md5;
            using (var inputStream = System.IO.File.Open(filePath, FileMode.Open, FileAccess.Read))
            {
                using (var md5Algorithm = System.Security.Cryptography.MD5.Create())
                {
                    md5 = md5Algorithm.ComputeHash(inputStream);
                }
            }
            return md5;
        }
        public byte[] GetMd5_(string filePath)
        {
            byte[] md5;
            using(var inputStream = new BufferedStream(File.OpenRead(filePath), 1200000))
            {
                using (var md5Algorithm = System.Security.Cryptography.MD5.Create())
                {
                    md5 = md5Algorithm.ComputeHash(inputStream);
                }
            }
            return md5;
        }
        public DateTime ToDateTime(string dateTimeAsString)
        {
            DateTime dateTime;
            // jpegInfo.DateTime has the format YYYY:MM:DD HH:MM:SS
            // DateTime parser needs the foramt YYYY.MM.DD HH:mM:SS
            var tmp = dateTimeAsString.ToCharArray();
            tmp[4] = '.';
            tmp[7] = '.';
            if (DateTime.TryParse(new string(tmp), out dateTime) == false)
                throw new Exception();

            return dateTime;
        }
        public string CreateDirectoryPathFrom(string dateTimeAsString)
        {
            DateTime dateTime;
            // jpegInfo.DateTime has the format YYYY:MM:DD HH:MM:SS
            // DateTime parser needs the foramt YYYY.MM.DD HH:mM:SS
            var tmp = dateTimeAsString.ToCharArray();
            tmp[4] = '.';
            tmp[7] = '.';
            if (DateTime.TryParse(new string(tmp), out dateTime) == false)
                throw new Exception();

            return CreateDirectoryPathFrom(dateTime);
        }
        public string CreateDirectoryPathFrom(DateTime dateTime)
        {
            return Path.Combine(
                    dateTime.Year.ToString(),
                    dateTime.Month.ToString("00"),
                    dateTime.Day.ToString("00"));
        }
        public void CopyFile(string source, string destination)
        {
            if (System.IO.File.Exists(destination) == false)
            {
                CreateDirectoryPath(destination);
                System.IO.File.Copy(source, destination);
            }
        }
        public void RemoveFile(string filePath)
        {
            if (System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
            }
        }
        public void CreateDirectoryPath(string targetPath)
        {
            if (Directory.Exists(Path.GetDirectoryName(targetPath)) == false)
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
        }
    }
}
