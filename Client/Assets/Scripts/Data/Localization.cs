using Table;

// strings 시트(Resources/Data/Strings.json)에서 현재 언어의 문자열을 가져온다.
// 시트의 name·textKey 컬럼 값은 표시 문자열이 아니라 stringID이므로, 화면에 보일 때는 반드시 Get()을 거친다.
public enum Language
{
	KR,
	EN,
	CN,
	TW,
	JP,
}

public static class Localization
{
	public static Language Current = Language.KR;

	// 현재 언어 문자열. 번역이 비어 있으면 한국어로 대체하고,
	// stringID 자체가 없으면 "[stringID]"를 돌려줘서 누락이 화면에서 눈에 띄게 한다.
	public static string Get(string stringID)
	{
		if (string.IsNullOrEmpty(stringID))
			return "";

		if (TableManager.Strings == null || !TableManager.Strings.TryGet(stringID, out StringsRow row))
			return $"[{stringID}]";

		string text = Pick(row, Current);
		return string.IsNullOrEmpty(text) ? row.KR : text;
	}

	static string Pick(StringsRow row, Language language)
	{
		switch (language)
		{
			case Language.EN: return row.EN;
			case Language.CN: return row.CN;
			case Language.TW: return row.TW;
			case Language.JP: return row.JP;
			default: return row.KR;
		}
	}
}
