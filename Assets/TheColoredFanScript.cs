using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using KModkit;
using UnityEngine.Serialization;
using Random = System.Random;

public class TheColoredFanScript : MonoBehaviour
{
	public KMBombInfo Bomb;
	public KMBombModule Module;
	public KMAudio Audio;
	public GameObject SmokeParticles;
	public ParticleSystem MistParticles;
	public GameObject fanBlades;
	public TextMesh displayText;
	public KMSelectable[] buttons;
	public KMColorblindMode ColorblindMode;
	public Material[] OpaqueMaterials;
	public Material[] TranslucentMaterials;
	public TextMesh[] SymbolColorTextMeshes;
	public TextMesh[] ButtonColorTextMeshes;
	public TextMesh FanColorTextMesh;

	private static int _moduleIdCounter = 1;
	private int moduleId;
	public bool isSolved;
	
	private float _fanSpeed;
	public float _fanTargetSpeed;
	private int _direction;
	private bool _isOn;

	private string[] _currentButtonColors;
	private string _currentFanBladeColor;
	private bool _fanBladeIsTranslucent;
	private bool _isColorblindMode;

	private Random _random = new Random();

	private static readonly string fanLetters = "AbcdefGHIjKlmnoPqRsTUVwXyz";
	private static readonly string[] ColorNames = { "Red", "Green", "Yellow", "Purple", "Blue", "Orange" };
	private static readonly int[][] ModificationOrders =
	{
		new[] {  7,  3,  2, -2, -3, -7 }, // inv mod 6 = 0
		new[] {  3,  2, -2, -3, -7,  7 }, // inv mod 6 = 1
		new[] {  2, -2, -3, -7,  7,  3 }, // inv mod 6 = 2
		new[] { -2, -3, -7,  7,  3,  2 }, // inv mod 6 = 3
		new[] { -3, -7,  7,  3,  2, -2 }, // inv mod 6 = 4
		new[] { -7,  7,  3,  2, -2, -3 }  // inv mod 6 = 5
	};
	
	private int[,] factorModificationTable =
	{
		{  1,  2,  3,  4,  5,  0 }, // Red
		{  2,  3,  4,  5,  0, -5 }, // Green
		{  3,  4,  5,  0, -5, -4 }, // Yellow
		{  4,  5,  0, -5, -4, -3 }, // Purple
		{  5,  0, -5, -4, -3, -2 }, // Blue
		{  0, -5, -4, -3, -2, -1 }, // Orange
	};

	private int[] _possibleFactors = { 3, 4, 6, 7, 8, 9, 10, 12 };
	private int[] _factorsToAvoid;
	[FormerlySerializedAs("initialSpeed")] public int _initialSpeed;
	private int _goalSpeed;
	private int[] _buttonValues;
	
	void Awake()
	{
		moduleId = _moduleIdCounter++;
        _direction = 1;
        _isOn = true;
        
        var initialMagnitude = _random.Next(0, 27);
        var initialSign = _random.Next(0, 2) == 0 ? 1 : -1;
        if (initialMagnitude == 0)
	        initialSign = 1;
        var goalSign = -initialSign;
        
        const int minDifference = 8;
        var goalMagnitude = _random.Next(0, 27);
        while (initialMagnitude + goalMagnitude < minDifference)
	        goalMagnitude++;
        _goalSpeed = goalSign * goalMagnitude;

        if (goalMagnitude == 0)
	        initialSign = -1;
        _initialSpeed = initialMagnitude * initialSign;
        
        if (_goalSpeed == 0 || _initialSpeed == 0)
	        _possibleFactors = _possibleFactors.Where(x => x != 7).ToArray();
        _factorsToAvoid = _possibleFactors.OrderBy(_ => _random.Next()).Take(_goalSpeed == 0 || _initialSpeed == 0 ? 4 : 3).ToArray();
        
        var availableMaterials = OpaqueMaterials.ToList();
        _currentButtonColors = new string[6];
        for (var i = 0; i < 6; i++)
        {
            var randIndex = UnityEngine.Random.Range(0, availableMaterials.Count);
            var buttonMaterial = availableMaterials[randIndex];
            buttons[i].GetComponent<MeshRenderer>().material = buttonMaterial;
            _currentButtonColors[i] = ColorNames[Array.IndexOf(OpaqueMaterials, buttonMaterial)];
            ButtonColorTextMeshes[i].text = _currentButtonColors[i][0].ToString();
            availableMaterials.RemoveAt(randIndex);
        }
        
        _currentFanBladeColor = ColorNames.PickRandom();
        FanColorTextMesh.text = _currentFanBladeColor[0].ToString();
        var currentFanBladeColorIndex = Array.IndexOf(ColorNames, _currentFanBladeColor);
        _fanBladeIsTranslucent = UnityEngine.Random.value > 0.5f;
        var fanMaterial = _fanBladeIsTranslucent ? TranslucentMaterials[currentFanBladeColorIndex] : OpaqueMaterials[currentFanBladeColorIndex];
        fanBlades.GetComponent<MeshRenderer>().material = fanMaterial;

        if (_fanBladeIsTranslucent)
        {
	        var temp = _initialSpeed;
	        _initialSpeed = _goalSpeed;
	        _goalSpeed = temp;
        }
        
        _fanSpeed = _initialSpeed;
        _fanTargetSpeed = _initialSpeed;

        _buttonValues = CalculateModifications(_currentButtonColors);
        for (var i = 0; i < buttons.Length; i++)
        {
	        var index = i;
	        buttons[i].OnInteract = () => ButtonPressed(index);
        }
        
        var symbols = new List<char>();
        if (_initialSpeed != 0)
	        symbols.Add(fanLetters[Math.Abs(_initialSpeed) - 1]);
        if (_goalSpeed != 0)
	        symbols.Add(fanLetters[Math.Abs(_goalSpeed) - 1]);

        var fanBladeColorIndex = Array.IndexOf(ColorNames, _currentFanBladeColor);
        var compToFanColor = fanBladeColorIndex % 2 == 0
	        ? ColorNames[fanBladeColorIndex + 1]
	        : ColorNames[fanBladeColorIndex - 1];
        var compToFanColorButtonIndex = Array.IndexOf(_currentButtonColors, compToFanColor);
        var goalSpeedColor = compToFanColorButtonIndex % 2 == 0 
	        ? _currentButtonColors[compToFanColorButtonIndex + 1]
	        : _currentButtonColors[compToFanColorButtonIndex - 1];
        var goalSpeedColorIndex = Array.IndexOf(ColorNames, goalSpeedColor);
        var initialSpeedColor = goalSpeedColorIndex % 2 == 0
	        ? ColorNames[goalSpeedColorIndex + 1]
	        : ColorNames[goalSpeedColorIndex - 1];

        if (_fanBladeIsTranslucent)
        {
	        var tempColor = initialSpeedColor;
	        initialSpeedColor = goalSpeedColor;
	        goalSpeedColor = tempColor;
        }
        
        var symbolColors = new List<string> ();
        if (_initialSpeed != 0)
	        symbolColors.Add(initialSpeedColor); 
        if (_goalSpeed != 0)
	        symbolColors.Add(goalSpeedColor);
        var factorColors = ColorNames.Where(x => x != initialSpeedColor && x != goalSpeedColor).OrderBy(_ => _random.Next()).ToArray();

	    var factorIndex = 0;
	    foreach (var factor in _factorsToAvoid)
        {
	        var displayValue = factor + factorModificationTable[fanBladeColorIndex, Array.IndexOf(ColorNames, factorColors[factorIndex])] - 1;
	        displayValue = (displayValue + 26) % 26;
	        symbols.Add(fanLetters[displayValue]);
	        symbolColors.Add(factorColors[factorIndex]);
	        factorIndex++;
        }

        char[] symbolsShuffled;
        string[] symbolColorsShuffled;
        ShuffleSymbols(symbols.ToArray(), symbolColors.ToArray(), out symbolsShuffled, out symbolColorsShuffled);
        SetSymbolColors(symbolsShuffled, symbolColorsShuffled);
        
        Debug.LogFormat("[The Colored Fan #{0}] Initial Speed is: {1}", moduleId, _initialSpeed);
        Debug.LogFormat("[The Colored Fan #{0}] Goal Speed is: {1}", moduleId, _goalSpeed);
        Debug.LogFormat("[The Colored Fan #{0}] Factors to avoid: {1}", moduleId, _factorsToAvoid.Join());
        Debug.LogFormat("[The Colored Fan #{0}] Button values: {1}", moduleId, _buttonValues.Join());
        Debug.LogFormat("[The Colored Fan #{0}] Symbol Letters: {1}", moduleId, symbols.Join().ToUpper());
        Debug.LogFormat("[The Colored Fan #{0}] Symbol Colors: {1}", moduleId, symbolColors.Join());
	}

	void Start ()
	{
		SetColorblindMode(ColorblindMode.ColorblindModeActive);
		StartCoroutine(Spin());
	}

	void SetColorblindMode(bool value)
	{
		_isColorblindMode = value;
		foreach (var mesh in ButtonColorTextMeshes)
			mesh.gameObject.SetActive(_isColorblindMode);
		foreach (var mesh in SymbolColorTextMeshes)
			mesh.gameObject.SetActive(_isColorblindMode);
		FanColorTextMesh.gameObject.SetActive(_isColorblindMode);
	}

	public static void ShuffleSymbols<T1, T2>(T1[] array1, T2[] array2, out T1[] shuffled1, out T2[] shuffled2)
	{
		if (array1.Length != array2.Length)
			throw new ArgumentException("Arrays must have the same length");

		Random rng = new Random();
		var shuffledIndices = Enumerable.Range(0, array1.Length)
			.OrderBy(_ => rng.Next())
			.ToArray();

		shuffled1 = shuffledIndices.Select(i => array1[i]).ToArray();
		shuffled2 = shuffledIndices.Select(i => array2[i]).ToArray();
	}
	
	private float _lastSoundTime = -1f; 
	IEnumerator Spin()
	{
		while (true)
		{
			if (!_isOn)
			{
				MistParticles.gameObject.SetActive(false);
				yield return null;
			}
			
			MistParticles.gameObject.SetActive(_fanTargetSpeed != 0);
			_fanSpeed = Mathf.Lerp(_fanSpeed, _fanTargetSpeed, Time.deltaTime);
			if (Math.Abs(_fanSpeed) < 0.05f && _fanTargetSpeed == 0)
				_fanSpeed = 0;
			_fanSpeed = Mathf.Clamp(_fanSpeed, -500, 500);
			fanBlades.transform.Rotate(Vector3.up, _fanSpeed * 0.7f);
			
			var main = MistParticles.main;
			main.startSpeed = Mathf.Clamp(Math.Abs(_fanTargetSpeed), 0.2f, 10f);
			main.startLifetime = Mathf.Clamp(1.5f / Mathf.Log(1 + Math.Abs(_fanTargetSpeed)), 0.5f, 1.5f);

			if (Math.Abs(_fanSpeed) > 0.05f)  // If fan is moving
			{
				float absFanSpeed = Mathf.Abs(_fanSpeed);
				
				float tickInterval = 3f / Mathf.Clamp(absFanSpeed, 0.01f, 30f);

				// If enough time has passed since the last sound, play it
				if (Time.time - _lastSoundTime >= tickInterval)
				{
					Audio.PlaySoundAtTransform("FanTick" + _random.Next(1, 3), fanBlades.transform);
					_lastSoundTime = Time.time;
				}
			}

			yield return null;
		}
	} 
	
	private int[] CalculateModifications(string[] permutedColors)
	{
		var ranks = new int[6];
		for (var i = 0; i < 6; i++)
			ranks[i] = Array.IndexOf(ColorNames, permutedColors[i]);
		var inversions = 0;
		for (var i = 0; i < 5; i++)
			for (var j = i + 1; j < 6; j++)
				if (ranks[i] > ranks[j])
					inversions++;
		var bluePosition = Array.IndexOf(permutedColors, "Blue");
		var modIndex = (inversions + bluePosition) % 6;
		return ModificationOrders[modIndex];
	}

	private bool ButtonPressed(int index)
	{
		buttons[index].AddInteractionPunch();
		Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, buttons[index].transform);
		if (isSolved) return false;
		_fanTargetSpeed += _buttonValues[index];
		Debug.LogFormat("[The Colored Fan #{0}] Speed changed by {1} to new speed {2}", moduleId, _buttonValues[index], _fanTargetSpeed);
		if (Math.Abs(_fanTargetSpeed) > 1000 || Math.Abs(_fanTargetSpeed - _goalSpeed) < 0.01)
		{
			_fanSpeed = Mathf.Min(_fanSpeed, 50);
			_fanTargetSpeed = 0;
			_isOn = false;
			isSolved = true;
			Audio.PlaySoundAtTransform("Bang", fanBlades.transform);
			SmokeParticles.gameObject.SetActive(true);
			Module.HandlePass();
			displayText.text = "";
			SetColorblindMode(false);
		}

		if (isSolved)
			return false;

		if (_factorsToAvoid.All(factor => Mathf.RoundToInt(_fanTargetSpeed) % factor != 0))
			return false;
		
		Debug.LogFormat("[The Colored Fan #{0}] Strike! {1} is divisible by {2}", moduleId, _fanTargetSpeed, _factorsToAvoid.First(factor => _fanTargetSpeed % factor == 0));		
		
		Module.HandleStrike();
		return false;
	}
	
	public void SetSymbolColors(char[] symbols, string[] colors)
	{
		var richText = "";
		for (var i = 0; i < symbols.Length; i++)
		{
			var hexColor = colorCodes[colors[i]];
			richText += "<color=#" + hexColor + ">" + symbols[i] + "</color>";
			SymbolColorTextMeshes[i].text = colors[i][0].ToString();
		}
		displayText.text = richText;
	}

#pragma warning disable 414
	private readonly string TwitchHelpMessage = @"!{0} input <colors> [r=red, g=green, y=yellow, p=purple, b=blue, o=orange], !{0} i <colors>, !{0} in <colors>, EXAMPLE: !1 input rgbrrbpyo";
#pragma warning restore 414
	
	IEnumerator ProcessTwitchCommand(string command)
	{
		command = command.ToLowerInvariant();
		var match = Regex.Match(command, @"^\s*(?:i|in|input)\s+([rgypbo]+)", RegexOptions.IgnoreCase);
		if (match.Success)
		{
			foreach (var index in match.Groups[1].Value.Select(c => Array.FindIndex(_currentButtonColors, color => char.ToLowerInvariant(color[0]) == c)))
				ButtonPressed(index);
			yield return true;
		}
		else if (Regex.IsMatch(command, @"^\s*colorblind\s*$", RegexOptions.IgnoreCase))
		{
			SetColorblindMode(true);
			yield return true;
		}
		yield return null;
	}

	private Dictionary<string, string> colorCodes = new Dictionary<string, string>()
	{
		{"Red", "D46161FF"},
		{"Green", "64C557FF"},
		{"Yellow", "DBC94FFF"},
		{"Purple", "BF60BDFF"},
		{"Blue", "7474CCFF"},
		{"Orange", "CC8A4EFF"},
	};
}
