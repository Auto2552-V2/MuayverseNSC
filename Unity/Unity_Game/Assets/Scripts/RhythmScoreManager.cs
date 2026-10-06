using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class RhythmScoreManager : MonoBehaviour
{
    private static RhythmScoreManager instance;

    [Header("Scoring")]
    [SerializeField] private int tapScore = 100;
    [SerializeField] private int catchScore = 100;
    [SerializeField] private int holdScore = 200;
    [SerializeField] private float holdTickIntervalSeconds = 0.5f;
    [SerializeField] private int holdScorePerSecond = 100;
    [SerializeField] private int comboBonusEvery = 10;
    [SerializeField] private int comboBonusScore = 50;
    [SerializeField] private bool resetScoreOnStart = true;

    [Header("UI")]
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private TMP_Text comboText;
    [SerializeField] private float scoreDisplayUnitsPerSecond;
    [SerializeField] private float comboPopScale = 1.35f;
    [SerializeField] private float comboPopRotationDegrees = 15f;
    [SerializeField] private float comboPopDurationSeconds = 0.18f;
    [SerializeField] private Vector2 comboPopPositionJitter = new Vector2(50f, 50f);
    [SerializeField] private float comboFadeDurationSeconds = 3f;
    [SerializeField] private Color[] comboPopColors =
    {
        Color.white,
        new Color(1f, 0.9f, 0.25f),
        new Color(0.3f, 1f, 0.9f),
        new Color(1f, 0.45f, 0.85f),
        new Color(0.55f, 1f, 0.35f)
    };
    [SerializeField] private bool createUiIfMissing = true;
    [SerializeField] private Vector2 uiOffset = new Vector2(32f, -32f);

    [Header("Events")]
    [SerializeField] private UnityEvent<int> onScoreChanged = new UnityEvent<int>();
    [SerializeField] private UnityEvent<int> onComboChanged = new UnityEvent<int>();

    private int score;
    private int displayedScore;
    private int combo;
    private int maxCombo;
    private float scoreDisplayAccumulator;
    private Coroutine comboPopRoutine;
    private Vector3 comboBaseScale = Vector3.one;
    private Quaternion comboBaseRotation = Quaternion.identity;
    private Vector2 comboBaseAnchoredPosition;
    private int comboPopDirection = 1;
    private int comboColorIndex;
    private float comboFadeTimer;
    private Color currentComboColor = Color.white;

    public static RhythmScoreManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindFirstObjectByType<RhythmScoreManager>();
            }

            if (instance == null)
            {
                GameObject scoreManagerObject = new GameObject("Rhythm Score Manager");
                instance = scoreManagerObject.AddComponent<RhythmScoreManager>();
            }

            return instance;
        }
    }

    public int Score => score;
    public float HoldTickIntervalSeconds => holdTickIntervalSeconds;
    public int HoldScorePerSecond => holdScorePerSecond;
    public int Combo => combo;
    public int MaxCombo => maxCombo;
    public UnityEvent<int> OnScoreChanged => onScoreChanged;
    public UnityEvent<int> OnComboChanged => onComboChanged;

    private void Update()
    {
        UpdateDisplayedScore();
        UpdateComboFade();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        EnsureUi();
        CenterComboPivot();
        CacheComboBaseScale();

        if (resetScoreOnStart)
        {
            ResetScore();
        }
        else
        {
            RefreshUi();
        }
    }

    public void ResetScore()
    {
        score = 0;
        displayedScore = 0;
        scoreDisplayAccumulator = 0f;
        combo = 0;
        maxCombo = 0;
        comboFadeTimer = 0f;
        ResetComboPop();
        RefreshUi();
        onScoreChanged.Invoke(score);
        onComboChanged.Invoke(combo);
    }

    public void RegisterHit(RhythmNoteType noteType)
    {
        AddCombo();
        score += GetBaseScore(noteType) + GetComboBonus();
        RefreshUi();
        onScoreChanged.Invoke(score);
        onComboChanged.Invoke(combo);
    }

    public void RegisterHoldTick()
    {
        AddCombo();
        RefreshUi();
        onComboChanged.Invoke(combo);
    }

    public void RegisterHoldScore(int scoreAmount)
    {
        if (scoreAmount <= 0)
        {
            return;
        }

        score += scoreAmount;
        RefreshUi();
        onScoreChanged.Invoke(score);
    }

    public void RegisterMiss()
    {
        if (combo == 0)
        {
            return;
        }

        combo = 0;
        ResetComboPop();
        RefreshUi();
        onComboChanged.Invoke(combo);
    }

    private int GetBaseScore(RhythmNoteType noteType)
    {
        switch (noteType)
        {
            case RhythmNoteType.Hold:
                return holdScore;
            case RhythmNoteType.Catch:
                return catchScore;
            default:
                return tapScore;
        }
    }

    private void AddCombo()
    {
        combo++;
        maxCombo = Mathf.Max(maxCombo, combo);
        comboFadeTimer = 0f;
        PlayComboPop();
    }

    private int GetComboBonus()
    {
        if (comboBonusEvery <= 0 || comboBonusScore <= 0)
        {
            return 0;
        }

        return combo / comboBonusEvery * comboBonusScore;
    }

    private float GetScoreDisplayUnitsPerSecond()
    {
        if (scoreDisplayUnitsPerSecond > 0f)
        {
            return scoreDisplayUnitsPerSecond;
        }

        if (holdTickIntervalSeconds <= 0f)
        {
            return 0f;
        }

        return holdScorePerSecond;
    }

    private void EnsureUi()
    {
        if (!createUiIfMissing || (scoreText != null && comboText != null))
        {
            return;
        }

        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasObject = new GameObject("Rhythm Score Canvas");
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.AddComponent<CanvasScaler>();
            canvasObject.AddComponent<GraphicRaycaster>();
        }

        Transform panel = CreateUiPanel(canvas.transform);
        if (scoreText == null)
        {
            scoreText = CreateUiText(panel, "Score Text", 0f, 32);
        }

        if (comboText == null)
        {
            comboText = CreateUiText(panel, "Combo Text", -42f, 26);
        }

        CenterComboPivot();
        CacheComboBaseScale();
    }

    private Transform CreateUiPanel(Transform parent)
    {
        GameObject panelObject = new GameObject("Score Combo UI", typeof(RectTransform));
        panelObject.transform.SetParent(parent, false);

        RectTransform rectTransform = panelObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0f, 1f);
        rectTransform.anchorMax = new Vector2(0f, 1f);
        rectTransform.pivot = new Vector2(0f, 1f);
        rectTransform.anchoredPosition = uiOffset;
        rectTransform.sizeDelta = new Vector2(340f, 96f);

        return panelObject.transform;
    }

    private TMP_Text CreateUiText(Transform parent, string objectName, float y, int fontSize)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform));
        textObject.transform.SetParent(parent, false);

        RectTransform rectTransform = textObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0f, 1f);
        rectTransform.anchorMax = new Vector2(0f, 1f);
        rectTransform.pivot = new Vector2(0f, 1f);
        rectTransform.anchoredPosition = new Vector2(0f, y);
        rectTransform.sizeDelta = new Vector2(340f, 38f);

        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Left;
        text.color = Color.white;
        text.raycastTarget = false;

        return text;
    }

    private void RefreshUi()
    {
        if (scoreText != null)
        {
            scoreText.text = $"SCORE {displayedScore}";
        }

        if (comboText != null)
        {
            comboText.text = combo > 0 ? $"COMBO {combo}" : string.Empty;
            if (combo <= 0)
            {
                SetComboAlpha(0f);
            }
        }
    }

    private void CenterComboPivot()
    {
        if (comboText == null)
        {
            return;
        }

        RectTransform comboRectTransform = comboText.rectTransform;
        Vector2 centeredPivot = new Vector2(0.5f, 0.5f);
        if (comboRectTransform.pivot == centeredPivot)
        {
            return;
        }

        Vector2 pivotDelta = centeredPivot - comboRectTransform.pivot;
        Rect rect = comboRectTransform.rect;
        comboRectTransform.pivot = centeredPivot;
        comboRectTransform.anchoredPosition += new Vector2(
            pivotDelta.x * rect.width,
            pivotDelta.y * rect.height);
    }

    private void UpdateDisplayedScore()
    {
        if (displayedScore == score)
        {
            scoreDisplayAccumulator = 0f;
            return;
        }

        float unitsPerSecond = GetScoreDisplayUnitsPerSecond();
        if (unitsPerSecond <= 0f)
        {
            displayedScore = score;
            RefreshUi();
            return;
        }

        scoreDisplayAccumulator += unitsPerSecond * Time.unscaledDeltaTime;
        int step = Mathf.Max(1, Mathf.FloorToInt(scoreDisplayAccumulator));
        if (step <= 0)
        {
            return;
        }

        scoreDisplayAccumulator -= step;
        displayedScore = Mathf.Min(score, displayedScore + step);
        RefreshUi();
    }

    private void CacheComboBaseScale()
    {
        if (comboText != null)
        {
            comboBaseScale = comboText.rectTransform.localScale;
            comboBaseRotation = comboText.rectTransform.localRotation;
            comboBaseAnchoredPosition = comboText.rectTransform.anchoredPosition;
        }
    }

    private void PlayComboPop()
    {
        if (comboText == null || !gameObject.activeInHierarchy)
        {
            return;
        }

        if (comboPopRoutine != null)
        {
            StopCoroutine(comboPopRoutine);
        }

        comboText.rectTransform.localScale = comboBaseScale;
        comboText.rectTransform.localRotation = comboBaseRotation;
        comboText.rectTransform.anchoredPosition = comboBaseAnchoredPosition + GetComboPositionJitter();
        comboPopDirection *= -1;
        ApplyNextComboColor();
        SetComboAlpha(1f);
        comboPopRoutine = StartCoroutine(AnimateComboPop());
    }

    private void ApplyNextComboColor()
    {
        if (comboPopColors == null || comboPopColors.Length == 0)
        {
            return;
        }

        currentComboColor = comboPopColors[comboColorIndex % comboPopColors.Length];
        comboText.color = WithAlpha(currentComboColor, 1f);
        comboColorIndex++;
    }

    private void UpdateComboFade()
    {
        if (combo <= 0 || comboText == null)
        {
            return;
        }

        comboFadeTimer += Time.unscaledDeltaTime;
        float duration = Mathf.Max(0.01f, comboFadeDurationSeconds);
        float alpha = 1f - Mathf.Clamp01(comboFadeTimer / duration);
        SetComboAlpha(alpha);

        if (comboFadeTimer < duration)
        {
            return;
        }

        combo = 0;
        comboFadeTimer = 0f;
        ResetComboPop();
        RefreshUi();
        onComboChanged.Invoke(combo);
    }

    private void SetComboAlpha(float alpha)
    {
        if (comboText != null)
        {
            comboText.color = WithAlpha(currentComboColor, alpha);
        }
    }

    private Color WithAlpha(Color color, float alpha)
    {
        color.a = Mathf.Clamp01(alpha);
        return color;
    }

    private void ResetComboPop()
    {
        if (comboPopRoutine != null)
        {
            StopCoroutine(comboPopRoutine);
            comboPopRoutine = null;
        }

        if (comboText != null)
        {
            comboText.rectTransform.localScale = comboBaseScale;
            comboText.rectTransform.localRotation = comboBaseRotation;
            comboText.rectTransform.anchoredPosition = comboBaseAnchoredPosition;
        }
    }

    private Vector2 GetComboPositionJitter()
    {
        return new Vector2(
            Random.Range(-comboPopPositionJitter.x, comboPopPositionJitter.x),
            Random.Range(-comboPopPositionJitter.y, comboPopPositionJitter.y));
    }

    private IEnumerator AnimateComboPop()
    {
        RectTransform comboRectTransform = comboText.rectTransform;
        float duration = Mathf.Max(0.01f, comboPopDurationSeconds);
        float halfDuration = duration * 0.5f;
        Vector3 popScale = comboBaseScale * comboPopScale;
        Quaternion popRotation = comboBaseRotation * Quaternion.Euler(0f, 0f, comboPopRotationDegrees * comboPopDirection);

        for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
        {
            float t = elapsed <= halfDuration
                ? elapsed / halfDuration
                : 1f - ((elapsed - halfDuration) / halfDuration);
            float eased = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
            comboRectTransform.localScale = Vector3.Lerp(comboBaseScale, popScale, eased);
            comboRectTransform.localRotation = Quaternion.Lerp(comboBaseRotation, popRotation, eased);
            yield return null;
        }

        comboRectTransform.localScale = comboBaseScale;
        comboRectTransform.localRotation = popRotation;
        comboPopRoutine = null;
    }

    private void OnValidate()
    {
        tapScore = Mathf.Max(0, tapScore);
        catchScore = Mathf.Max(0, catchScore);
        holdScore = Mathf.Max(0, holdScore);
        holdTickIntervalSeconds = Mathf.Max(0.01f, holdTickIntervalSeconds);
        holdScorePerSecond = Mathf.Max(0, holdScorePerSecond);
        comboBonusEvery = Mathf.Max(0, comboBonusEvery);
        comboBonusScore = Mathf.Max(0, comboBonusScore);
        scoreDisplayUnitsPerSecond = Mathf.Max(0f, scoreDisplayUnitsPerSecond);
        comboPopScale = Mathf.Max(1f, comboPopScale);
        comboPopRotationDegrees = Mathf.Clamp(comboPopRotationDegrees, 0f, 15f);
        comboPopDurationSeconds = Mathf.Max(0.01f, comboPopDurationSeconds);
        comboPopPositionJitter = new Vector2(
            Mathf.Max(0f, comboPopPositionJitter.x),
            Mathf.Max(0f, comboPopPositionJitter.y));
        comboFadeDurationSeconds = Mathf.Max(0.01f, comboFadeDurationSeconds);
    }
}
