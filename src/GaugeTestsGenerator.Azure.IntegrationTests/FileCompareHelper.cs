using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GaugeTestsGenerator.Azure.IntegrationTests
{
    public static class FileCompareHelper
    {
        public static void AssertDirectoriesIdentical(string pathDir1, string pathDir2)
        {
            List<string> lstErrors = new List<string>();
            System.IO.DirectoryInfo dir1 = new System.IO.DirectoryInfo(pathDir1);
            System.IO.DirectoryInfo dir2 = new System.IO.DirectoryInfo(pathDir2);

            // Take a snapshot of the file system.  
            IEnumerable<System.IO.FileInfo> list1 = dir1.GetFiles("*.*", System.IO.SearchOption.AllDirectories);
            IEnumerable<System.IO.FileInfo> list2 = dir2.GetFiles("*.*", System.IO.SearchOption.AllDirectories);

            //A custom file comparer defined below  
            FileCompare myFileCompare = new FileCompare();

            // This query determines whether the two folders contain  
            // identical file lists, based on the custom file comparer  
            // that is defined in the FileCompare class.  
            // The query executes immediately because it returns a bool.  
            bool areIdentical = list1.SequenceEqual(list2, myFileCompare);

            if (areIdentical == true)
            {
                Console.WriteLine("the two folders are the same");
            }
            else
            {
                lstErrors.Add("The two folders are not the same");
            }

            // Find the common files. It produces a sequence and doesn't
            // execute until the foreach statement.  
            var queryCommonFiles = list1.Intersect(list2, myFileCompare);

            if (queryCommonFiles.Any())
            {
                Console.WriteLine("The following files are in both folders:");
                foreach (var v in queryCommonFiles)
                {
                    Console.WriteLine(v.FullName); //shows which items end up in result list  
                }
            }
            else
            {
                lstErrors.Add("There are no common files in the two folders.");
            }

            // Find the set difference between the two folders. 
            var queryList1Only = (from file in list1
                                  select file).Except(list2, myFileCompare);

            if (queryList1Only.Any())
            {
                lstErrors.Add("The following files are in list1 but not list2:");
                foreach (var v in queryList1Only)
                {
                    lstErrors.Add(v.FullName);
                }
            }

            // Find the set difference between the two folders. 
            var queryList2Only = (from file in list2
                                  select file).Except(list1, myFileCompare);

            if (queryList2Only.Any())
            {
                lstErrors.Add("The following files are in list2 but not list1:");
                foreach (var v in queryList2Only)
                {
                    lstErrors.Add(v.FullName);
                }
            }

            if (lstErrors.Count > 0)
                throw new Exception(string.Join(Environment.NewLine, lstErrors));
        }

        // This implementation defines a very simple comparison  
        // between two FileInfo objects. It only compares the name  
        // of the files being compared and their length in bytes.  
        class FileCompare : System.Collections.Generic.IEqualityComparer<System.IO.FileInfo>
        {
            public FileCompare() { }

            public bool Equals(System.IO.FileInfo f1, System.IO.FileInfo f2)
            {
                return (f1.Name == f2.Name &&
                        f1.Length == f2.Length);
            }

            // Return a hash that reflects the comparison criteria. According to the
            // rules for IEqualityComparer<T>, if Equals is true, then the hash codes must  
            // also be equal. Because equality as defined here is a simple value equality, not  
            // reference identity, it is possible that two or more objects will produce the same  
            // hash code.  
            public int GetHashCode(System.IO.FileInfo fi)
            {
                string s = $"{fi.Name}{fi.Length}";
                return s.GetHashCode();
            }
        }
    }
}
