using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace KSArmory.Feedback;

/// <summary>
/// Scores text for toxicity locally, with no network call.
///
/// <para>The model is <c>unitary/toxic-bert</c>, Apache-2.0, exported to ONNX during the image
/// build from the original weights rather than pulled from a stranger's re-upload. It is
/// multi-label: six independent sigmoid outputs, not a softmax over six classes, so a comment can
/// be both an insult and a threat and the scores do not sum to one.</para>
///
/// <para>Local because every hosted alternative is a dependency that can be withdrawn — Perspective
/// ran for nine years and announced its own end date — and because nothing anyone types then leaves
/// the machine.</para>
/// </summary>
public sealed class Classifier : IDisposable
{
    /// <summary>Detoxify's label order. Fixed by the model, not a choice.</summary>
    private static readonly string[] Labels =
        ["toxic", "severe_toxic", "obscene", "threat", "insult", "identity_hate"];

    // BERT's positional embeddings stop here, and the tokenizer will happily hand over more.
    private const int MaxTokens = 512;

    private readonly InferenceSession _session;
    private readonly BertTokenizer _tokenizer;
    private readonly string _inputIds;
    private readonly string _attentionMask;
    private readonly bool _wantsTokenTypes;

    private Classifier(InferenceSession session, BertTokenizer tokenizer)
    {
        _session = session;
        _tokenizer = tokenizer;

        // Input names differ between exporters, so they are read off the model rather than assumed.
        List<string> inputs = [.. session.InputMetadata.Keys];
        _inputIds = inputs.FirstOrDefault(n => n.Contains("input_ids")) ?? "input_ids";
        _attentionMask = inputs.FirstOrDefault(n => n.Contains("attention_mask")) ?? "attention_mask";
        _wantsTokenTypes = inputs.Any(n => n.Contains("token_type_ids"));
    }

    /// <summary>
    /// Loads the model, or returns null when it is not present.
    ///
    /// <para>Absent is a normal state: the service runs without a classifier and says so, rather
    /// than refusing to start over an optional dependency.</para>
    /// </summary>
    public static Classifier? TryLoad(string directory, ILogger log)
    {
        string model = Path.Combine(directory, "model.onnx");
        string vocab = Path.Combine(directory, "vocab.txt");

        if (!File.Exists(model) || !File.Exists(vocab))
        {
            log.LogInformation("no local classifier at {Directory}", directory);
            return null;
        }

        try
        {
            // One thread: the box runs a database and two other services, and a report is a
            // sentence. Latency here is already a few milliseconds.
            var options = new Microsoft.ML.OnnxRuntime.SessionOptions
            {
                IntraOpNumThreads = 1,
                InterOpNumThreads = 1,
            };

            using FileStream vocabStream = File.OpenRead(vocab);
            var tokenizer = BertTokenizer.Create(vocabStream);

            var classifier = new Classifier(new InferenceSession(model, options), tokenizer);
            log.LogInformation("local classifier loaded from {Directory}", directory);
            return classifier;
        }
        catch (Exception e)
        {
            log.LogError("could not load the classifier: {Message}", e.Message);
            return null;
        }
    }

    /// <summary>The highest label score, and which label it was.</summary>
    public (string Label, float Score) Worst(string text)
    {
        IReadOnlyList<int> ids = _tokenizer.EncodeToIds(text);
        int length = Math.Min(ids.Count, MaxTokens);

        var inputIds = new DenseTensor<long>([1, length]);
        var mask = new DenseTensor<long>([1, length]);
        var types = new DenseTensor<long>([1, length]);

        for (int i = 0; i < length; i++)
        {
            inputIds[0, i] = ids[i];
            mask[0, i] = 1;
            types[0, i] = 0;
        }

        List<NamedOnnxValue> feeds =
        [
            NamedOnnxValue.CreateFromTensor(_inputIds, inputIds),
            NamedOnnxValue.CreateFromTensor(_attentionMask, mask),
        ];

        if (_wantsTokenTypes) feeds.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", types));

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run(feeds);
        float[] logits = [.. results.First().AsEnumerable<float>()];

        string worst = Labels[0];
        float highest = 0f;

        for (int i = 0; i < logits.Length && i < Labels.Length; i++)
        {
            // Sigmoid per label: the head is multi-label, so a softmax here would make six
            // independent probabilities compete and quietly suppress all but the strongest.
            float score = 1f / (1f + MathF.Exp(-logits[i]));
            if (score <= highest) continue;

            highest = score;
            worst = Labels[i];
        }

        return (worst, highest);
    }

    public void Dispose() => _session.Dispose();
}
