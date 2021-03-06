﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MyMediaLite.Data;
using System.IO;
using System.Diagnostics;

namespace RF2.Recommenders
{
    public class LibFmTrainTester : ITrainTester<ItemRating>
    {
        // users and items should be in the same map because libFm views them in the same way 
        // i.e. it can not distinguesh which feature is user and which feature is item
        // therefore there shouldn't be duplicate mapped id's between users and items
        Mapping _usersItemsMap;
        
        // path to a folder to save temprorary converted files
        string _dataStorePath;

        // libFm parameters
        public string LibFmPath { get; set; }
        public double LearningRate { get; set; }
        public int Iterations { get; set; }
        public string Dimensions { get; set; }
        public string Regularization { get; set; }
        public FmLearnigAlgorithm LearningAlgorithm { get; set; }
        int _i = 0;

        public LibFmTrainTester(int i = 0)
            : this("")
        {
            _i = i;
        }

        public LibFmTrainTester(string dataStorePath,
            string libFmPath = "libFm.exe",
            double learningRate = 0.05, 
            int numIterations = 30, 
            string dimensions = "1,1,8", 
            FmLearnigAlgorithm alg = FmLearnigAlgorithm.MCMC,
            string regularization = "0,0,0.1")
        {
            _usersItemsMap = new Mapping();
            _dataStorePath = !String.IsNullOrEmpty(dataStorePath) && dataStorePath.Last() != '\\' ? dataStorePath + "\\" : dataStorePath;

            // default properties
            LibFmPath = libFmPath;
            LearningRate = learningRate;
            Iterations = numIterations;
            Dimensions = dimensions;
            LearningAlgorithm = alg;
            Regularization = regularization;
        }

        public void TrainAndTest(IEnumerable<ItemRating> trainSet, IEnumerable<ItemRating> testSet)
        {
            string trainPath = _dataStorePath + "train.libfm";
            string testPath = _dataStorePath + "test.libfm";
            string testOutput = _dataStorePath + "test.out." + _i;
            
            // converting train and test data to libFm files becuase libfm.exe only get file names as input
            SaveLibFmFile(trainSet, trainPath);
            SaveLibFmFile(testSet, testPath);

            // initialize the process
            var libFm = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = LibFmPath,
                    Arguments = BuildArguments(trainPath, testPath, testOutput),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            string libFMTrainRMSE = "", libFMTestRMSE = "";
            double lowestRMSE = double.MaxValue;
            int iter = 0, lowestIteration = 0;

            libFm.OutputDataReceived += (p, dataLine) =>
            {
                var data = dataLine.Data;

                if (data != null && (data.StartsWith("Loading") || data.StartsWith("#")))
                {
                    Console.WriteLine(dataLine.Data);

                    if (data.StartsWith("#Iter"))
                    {
                        libFMTrainRMSE = data.Substring(data.IndexOf("Train") + 6, 6);
                        libFMTestRMSE = data.Substring(data.IndexOf("Test") + 5);

                        double current = double.Parse(libFMTestRMSE);

                        if (current < lowestRMSE)
                        {
                            lowestRMSE = current;
                            lowestIteration = iter;
                        }

                        iter++;
                    }
                }
            };

            libFm.Start();
            libFm.BeginOutputReadLine();
            libFm.WaitForExit();

            Console.WriteLine("Lowest RMSE on test set reported by LibFm is: {0:0.0000} at iteration {1}", lowestRMSE, lowestIteration);
            UpdateTestSet(testSet, testOutput);

            // write actual ratings in the test set
            File.WriteAllLines(_dataStorePath + "test.act", testSet.Select(ir => ir.Rating.ToString()).ToList());
        }

        private void SaveLibFmFile(IEnumerable<ItemRating> dataset, string fileName)
        {
            var output = dataset.Select(ir => ir.ToLibFmFeatureVector(_usersItemsMap)).ToList();
            File.WriteAllLines(fileName, output);
        }

        private string BuildArguments(string trainFile, string testFile, string testOutput)
        {
            return String.Format("-task r -train {0} -test {1} -method {2} -iter {3} -dim {4} -learn_rate {5} -out {6} -regular {7}",
                trainFile, testFile, LearningAlgorithm.ToString().ToLower(), Iterations, Dimensions, LearningRate, testOutput, Regularization);
        }

        private void UpdateTestSet(IEnumerable<ItemRating> testSet, string testOutput)
        {
            var predictedRatings = File.ReadAllLines(testOutput).ToList();

            // it is important that the order of test samples and predicted ratings in the output file remains the same
            // the testSet should already be a list to make sure that the updates on items applies on the original set
            int i = 0;
            foreach (var itemRating in testSet)
            {
                itemRating.PredictedRating = float.Parse(predictedRatings[i++]);
            }
        }

    }

    public enum FmLearnigAlgorithm
    {
        MCMC,
        SGD,
        SGDA,
        ALS
    }
}
