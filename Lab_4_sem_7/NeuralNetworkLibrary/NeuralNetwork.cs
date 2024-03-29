﻿using BERTTokenizers;
using System.Net;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BERTTokenizers.Base;

namespace MyApp
{
    public class NeuralNetwork
    {
        static Semaphore sessionRunLock = new Semaphore(1, 1);
        private ModelDownload downloadInterface;
        private InferenceSession session;
        public bool isDownloaded;
        // Create Tokenizer and tokenize the sentence.
        public NeuralNetwork() 
        { 
            downloadInterface = new ModelDownload();
            session = null;
            isDownloaded = false;
        }
        public async Task<bool> OnnxModelInit(CancellationToken token = default) {
            string modelPath = "C:\\Users\\Yakov\\Downloads\\study_7_sem\\bert-large-uncased-whole-word-masking-finetuned-squad.onnx";
            ModelDownload.DownloadOnnxModel(modelPath);
            if (token.IsCancellationRequested)
            {
                return false;
            }
            session = new InferenceSession(modelPath);
            isDownloaded = true;
            return true;
        }
        public async Task<String> AnswerQuestionAsync(string fileText, string question, CancellationToken token = default)
        {
            if (isDownloaded == false) {
                throw new NotSupportedException("Model isn't downloaded.");
            }
            return await Task.Factory.StartNew<string>(_ =>
            {
                var sentence = $"{{\"question\": \"{question}\", \"context\": \"{fileText}\"}}";

                // Create Tokenizer and tokenize the sentence.
                var tokenizer = new BertUncasedLargeTokenizer();

                // Get the sentence tokens.
                var tokens = tokenizer.Tokenize(sentence);
                // Console.WriteLine(String.Join(", ", tokens));

                // Encode the sentence and pass in the count of the tokens in the sentence.
                var encoded = tokenizer.Encode(tokens.Count(), sentence);

                // Break out encoding to InputIds, AttentionMask and TypeIds from list of (input_id, attention_mask, type_id).
                var bertInput = new BertInput()
                {
                    InputIds = encoded.Select(t => t.InputIds).ToArray(),
                    AttentionMask = encoded.Select(t => t.AttentionMask).ToArray(),
                    TypeIds = encoded.Select(t => t.TokenTypeIds).ToArray(),
                };

                // Create input tensor.

                var input_ids = ConvertToTensor(bertInput.InputIds, bertInput.InputIds.Length);
                var attention_mask = ConvertToTensor(bertInput.AttentionMask, bertInput.InputIds.Length);
                var token_type_ids = ConvertToTensor(bertInput.TypeIds, bertInput.InputIds.Length);


                // Create input data for session.
                var input = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("input_ids", input_ids),
                    NamedOnnxValue.CreateFromTensor("input_mask", attention_mask),
                    NamedOnnxValue.CreateFromTensor("segment_ids", token_type_ids) };

                // Run session and send the input data in to get inference output. 
                if (token.IsCancellationRequested)  
                {
                    throw new ObjectDisposedException("cts");
                }
                IDisposableReadOnlyCollection<DisposableNamedOnnxValue> output;
                sessionRunLock.WaitOne();
                try
                {
                    output = session.Run(input);
                } 
                catch (Exception ex)
                {
                    sessionRunLock.Release();
                    throw;
                }
                sessionRunLock.Release();
                if (token.IsCancellationRequested)
                {
                    throw new ObjectDisposedException("cts");
                }
                // Call ToList on the output.
                // Get the First and Last item in the list.
                // Get the Value of the item and cast as IEnumerable<float> to get a list result.
                List<float> startLogits = (output.ToList().First().Value as IEnumerable<float>).ToList();
                List<float> endLogits = (output.ToList().Last().Value as IEnumerable<float>).ToList();

                // Get the Index of the Max value from the output lists.
                var startIndex = startLogits.ToList().IndexOf(startLogits.Max());
                var endIndex = endLogits.ToList().IndexOf(endLogits.Max());

                // From the list of the original tokens in the sentence
                // Get the tokens between the startIndex and endIndex and convert to the vocabulary from the ID of the token.
                var predictedTokens = tokens
                            .Skip(startIndex)
                            .Take(endIndex + 1 - startIndex)
                            .Select(o => tokenizer.IdToToken((int)o.VocabularyIndex))
                            .ToList();

                // Print the result.
                if (token.IsCancellationRequested)
                {
                    throw new ObjectDisposedException("cts");
                }
                return String.Join(" ", predictedTokens);
            }, token, TaskCreationOptions.LongRunning);
        }
        // Get the sentence tokens.
        // var tokens = tokenizer.Tokenize(sentence);
        static Tensor<long> ConvertToTensor(long[] inputArray, int inputDimension)
        {
            // Create a tensor with the shape the model is expecting. Here we are sending in 1 batch with the inputDimension as the amount of tokens.
            Tensor<long> input = new DenseTensor<long>(new[] { 1, inputDimension });

            // Loop through the inputArray (InputIds, AttentionMask and TypeIds)
            for (var i = 0; i < inputArray.Length; i++)
            {
                // Add each to the input Tenor result.
                // Set index and array value of each input Tensor.
                input[0, i] = inputArray[i];
            }
            return input;
        }
    }
    public class BertInput
    {
        public long[] InputIds { get; set; }
        public long[] AttentionMask { get; set; }
        public long[] TypeIds { get; set; }
    }
}