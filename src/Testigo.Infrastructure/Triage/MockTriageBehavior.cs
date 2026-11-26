using System.Text.Json;
using System.Text.RegularExpressions;
using AgentRuntime.Infrastructure.Llm;
using AgentRuntime.LLM;
using Testigo.Triage;

namespace Testigo.Infrastructure.Triage;

/// <summary>
/// The Mock provider's answer to a triage request, so <c>docker compose up</c> shows the whole
/// pipeline without a model. It's a fixed keyword matcher, deterministic and honest about it:
/// every quote is an exact sentence of the redacted report, so grounding passes, but its
/// classifications say nothing about how well a real model does. Evals with a local model
/// (NFR-AI-1) measure that.
/// </summary>
public sealed class MockTriageBehavior : IMockLlmBehavior
{
    // Cues are folded (lower case, no accents). Order matters for single-choice dimensions.
    private static readonly (string Dimension, string Term, string[] Cues)[] Cues =
    [
        ("technology", "neuro_consumer_eeg", ["ondas cerebrales", "casco", "diadema", "vincha", "electroencefal", "eeg", "sensores en la frente", "banda en la cabeza"]),
        ("technology", "neuro_bci", ["implante cerebral", "interfaz cerebro", "electrodos implantados", "chip en el cerebro"]),
        ("technology", "neuro_stimulation", ["estimulacion cerebral", "estimulacion magnetica", "estimulacion transcraneal", "electroestimulacion", "tdcs"]),
        ("technology", "neuro_data_app", ["datos cerebrales", "datos neuronales", "app de meditacion", "app de sueno", "app de concentracion"]),
        ("technology", "ai_emotion_recognition", ["reconocimiento de emociones", "detectar emociones", "detecta emociones", "estado de animo", "expresiones faciales", "emociones"]),
        ("technology", "ai_biometric_recognition", ["reconocimiento facial", "biometric", "huella digital", "huella dactilar", "escanear mi cara", "rostro"]),
        ("technology", "ai_automated_decision", ["decision automatica", "algoritmo", "puntaje", "sistema automatico", "evaluacion automatica", "score"]),
        ("technology", "ai_generative", ["deepfake", "imagen falsa", "video falso", "fotos falsas", "voz clonada", "generada con inteligencia artificial", "generado con ia", "montaje"]),
        ("technology", "ai_recommender", ["recomendaciones", "me recomendaba", "contenido que me mostraba", "el feed"]),
        ("technology", "ai_workplace_monitoring", ["mide nuestra productividad", "productividad", "control de jornada", "monitoreo", "monitorea"]),
        ("technology", "ai_chatbot_agent", ["chatbot", "asistente virtual", "un bot", "el bot", "chat automatico"]),

        ("harm_type", "data_collection_without_consent", ["sin consentimiento", "sin mi consentimiento", "nadie nos pidio", "nadie me pidio", "sin permiso", "sin autorizacion", "nunca acepte", "sin avisar"]),
        ("harm_type", "data_reuse_or_sale", ["vendieron", "vendian", "venta de datos", "compartieron mis datos", "entregaron mis datos", "a terceros"]),
        ("harm_type", "surveillance", ["vigila", "vigilancia", "nos graban", "me graban", "camaras", "rastreo", "seguimiento"]),
        ("harm_type", "discrimination", ["discrimin", "por ser mujer", "por mi edad", "por ser mapuche", "por ser migrante", "por mi acento", "color de piel", "por mi apellido"]),
        ("harm_type", "wrongful_automated_decision", ["me rechazaron", "me negaron", "me nego", "me desactivaron", "me bloquearon", "me descartaron", "sin explicacion"]),
        ("harm_type", "manipulation", ["manipul", "adictiv", "enganchar", "no podia dejar"]),
        ("harm_type", "psychological_harm", ["ansiedad", "angustia", "depresion", "no duermo", "estres", "verguenza", "humill"]),
        ("harm_type", "reputational_harm", ["reputacion", "difundieron", "circulo por", "todos lo vieron"]),
        ("harm_type", "financial_harm", ["perdi mi trabajo", "perdi mis ingresos", "perdi ingresos", "deuda", "me cobraron", "me despidieron", "sueldo"]),
        ("harm_type", "loss_of_service", ["me cortaron", "perdi el acceso", "sin acceso", "me quitaron el beneficio", "no pude acceder", "suspendieron"]),
        ("harm_type", "physical_harm", ["dolor de cabeza", "dolores de cabeza", "quemadura", "mareos", "lesion", "convulsion"]),

        ("rights", "privacy", ["privacidad", "datos personales", "intimidad", "mis datos"]),
        ("rights", "mental_integrity", ["ondas cerebrales", "datos cerebrales", "pensamientos", "mi mente", "cerebro"]),
        ("rights", "non_discrimination", ["discrimin", "por ser mujer", "por ser mapuche", "por ser migrante", "color de piel"]),
        ("rights", "due_process", ["sin explicacion", "no pude apelar", "no me dejaron reclamar", "nadie me explico", "a quien reclamar"]),
        ("rights", "expression", ["censur", "borraron mi publicacion", "libertad de expresion", "me silenciaron"]),
        ("rights", "health", ["salud", "medico", "clinica", "hospital", "tratamiento"]),
        ("rights", "work", ["trabajo", "empleo", "laboral", "postule", "despid"]),

        ("affected", "children", ["mi hijo", "mi hija", "nino", "nina", "estudiantes", "alumnos", "menores"]),
        ("affected", "workers", ["trabajadores", "companeros de trabajo", "repartidores", "conductores", "operarios"]),
        ("affected", "patients", ["pacientes", "paciente"]),
        ("affected", "indigenous", ["mapuche", "aymara", "pueblo originario", "indigena", "rapa nui"]),
        ("affected", "migrants", ["migrante", "extranjero", "permiso de residencia"]),
        ("affected", "group", ["todos los trabajadores", "todos los vecinos", "somos varios", "varios de", "la comunidad", "los vecinos"]),

        ("actor_type", "employer", ["mi empleador", "donde trabajo", "mi jefe", "mi jefa", "trabajo en", "mi trabajo"]),
        ("actor_type", "government", ["municipalidad", "ministerio", "gobierno", "servicio publico", "registro civil"]),
        ("actor_type", "health_provider", ["clinica", "hospital", "centro medico", "consultorio"]),
        ("actor_type", "school", ["colegio", "liceo", "universidad", "escuela"]),
        ("actor_type", "company", ["empresa", "aplicacion", "la app", "una app", "plataforma", "banco", "tienda"]),
    ];

    private static readonly string[] DangerCues = ["tengo miedo", "me amenaz", "nos amenaz", "peligro", "me quiero morir", "no quiero vivir", "hacerme dano", "me siguen", "sabe donde vivo"];

    private static readonly Regex Actor = new(
        @"(?<![\p{L}])(?:\p{Lu}[\p{L}\d]+\s){0,3}\p{Lu}[\p{L}\d]+\s(?:SpA|S\.A\.|Ltda\.?|Limitada)(?![\p{L}])|(?<=\b(?:empresa|aplicaci[oó]n|app|plataforma|banco|municipalidad de|cl[ií]nica|colegio)\s)(?:\p{Lu}[\p{L}\d]+)(?:\s\p{Lu}[\p{L}\d]+){0,3}",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Place = new(@"\b(?:Chile|Argentina|Per[uú]|Bolivia|Brasil|Colombia|M[eé]xico|Santiago|Valpara[ií]so|Concepci[oó]n|Temuco|Antofagasta|Iquique|Arica|La Serena|Puerto Montt|Punta Arenas|Rancagua|Talca|Valdivia|Osorno)\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Date = new(@"\b(?:(?:en|desde|el)\s(?:\d{1,2}\sde\s)?(?:enero|febrero|marzo|abril|mayo|junio|julio|agosto|septiembre|octubre|noviembre|diciembre)(?:\sde\s(?:19|20)\d{2})?|(?:en|desde)\s(?:19|20)\d{2})\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
    private static readonly Regex Nickname = new(@"\b(?:el|la)\s(?:t[ií][oa]|profe|profesora?|supervisora?|jef[ea]|encargad[oa])\s(\p{Lu}\p{Ll}+)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public bool Handles(LlmCompletionRequest request) => request.Tools.Count == 1 && request.Tools[0].Name == TriagePrompt.ToolName;

    public LlmCompletionResponse Respond(LlmCompletionRequest request)
    {
        var report = TriagePrompt.ReportOf(request) ?? string.Empty;
        return new LlmCompletionResponse
        {
            ToolCalls = [new ToolCall { Id = "mock_triage", Name = TriagePrompt.ToolName, ArgumentsJson = JsonSerializer.Serialize(Analyze(report)) }],
            FinishReason = LlmFinishReason.ToolCalls,
            InputTokens = report.Length / 4 + 600,
            OutputTokens = 200,
        };
    }

    internal static object Analyze(string report)
    {
        var folded = FoldedText.Of(report);
        var classifications = new List<object>();
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (dimension, term, cues) in Cues)
        {
            if (dimension == "actor_type" && found.Contains(dimension)) continue;
            if (Find(report, folded, cues) is not { } quote) continue;
            classifications.Add(new { dimension, term, quote });
            found.Add(dimension);
        }

        if (!found.Contains("affected") && FirstSentence(report) is { } first)
        {
            classifications.Add(new { dimension = "affected", term = "individual", quote = first });
        }

        var fields = new List<object>();
        AddField(fields, report, "actor_name", Actor.Match(report));
        AddField(fields, report, "place", Place.Match(report));
        AddField(fields, report, "date", Date.Match(report));

        var danger = Find(report, folded, DangerCues);
        var personal = Nickname.Matches(report).Select(m => new { text = m.Groups[1].Value, type = "PERSON" }).DistinctBy(p => p.text).ToList();

        return new
        {
            language = LanguageDetector.Detect(report) is "und" ? "other" : LanguageDetector.Detect(report),
            classifications,
            fields,
            immediate_danger = danger is null ? (object)new { flag = false } : new { flag = true, quote = danger },
            personal_data = personal,
        };
    }

    private static void AddField(List<object> fields, string report, string field, Match m)
    {
        if (!m.Success) return;
        var value = m.Value.Trim();
        if (SentenceAround(report, m.Index, m.Length) is { } quote) fields.Add(new { field, value, quote });
    }

    private static string? Find(string report, FoldedText folded, string[] cues)
    {
        var best = -1;
        var bestLength = 0;
        foreach (var cue in cues)
        {
            var at = folded.Folded.IndexOf(cue, StringComparison.Ordinal);
            if (at >= 0 && (best < 0 || at < best))
            {
                best = at;
                bestLength = cue.Length;
            }
        }

        if (best < 0) return null;
        var (start, length) = folded.ToOriginal(best, bestLength);
        return SentenceAround(report, start, length);
    }

    /// <summary>The sentence around a match, at most 280 characters, copied exactly.</summary>
    private static string? SentenceAround(string text, int at, int length)
    {
        var start = at;
        while (start > 0 && ".!?\n".IndexOf(text[start - 1]) < 0 && at - start < 200) start--;
        while (start < at && char.IsWhiteSpace(text[start])) start++;
        var end = at + length;
        while (end < text.Length && ".!?\n".IndexOf(text[end]) < 0 && end - start < 280) end++;
        var quote = text[start..end].Trim();
        return quote.Length >= QuoteLocator.MinimumLength ? quote : null;
    }

    private static string? FirstSentence(string text)
    {
        var end = text.IndexOfAny(['.', '!', '?', '\n']);
        var s = (end < 0 ? text : text[..end]).Trim();
        if (s.Length > 280) s = s[..280];
        return s.Length >= QuoteLocator.MinimumLength ? s : null;
    }
}
