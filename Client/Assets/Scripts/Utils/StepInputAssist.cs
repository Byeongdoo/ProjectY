using UnityEngine;

// 칸 이동 중 입력 보정 규칙. 이승(싱글)과 삼천도·저승(멀티) 컨트롤러가 같은 반응을 내도록 함께 쓴다.
// 서버·패킷에 의존하지 않는다. 갈 수 있는지(벽·점유)는 호출 쪽이 판정한다.
//
// - 직선 한 칸을 시작하고 window(configs 20048) 안에 두 번째 방향키가 들어오면 같은 출발 칸에서 대각선 칸으로 목표를 바꾼다.
// - 진행 방향의 정반대를 누르면 출발 칸으로 되돌아간다.
// 둘 다 한 칸에 한 번만 적용한다(되돌아가기를 반복해 제자리에서 떨리지 않게).
public static class StepInputAssist
{
	public enum Kind { None, Reverse, Diagonal }

	public static Kind TryAdjust(Vector3Int from, Vector3Int dest, int inputX, int inputY, float elapsed, float window,
		out Vector3Int newFrom, out Vector3Int newDest)
	{
		newFrom = from; newDest = dest;
		if (inputX == 0 && inputY == 0)
			return Kind.None;

		Vector3Int step = dest - from;
		Vector3Int input = new Vector3Int(inputX, inputY, 0);

		if (input == -step)
		{
			newFrom = dest; newDest = from;
			return Kind.Reverse;
		}

		bool straight = step.x == 0 || step.y == 0;
		bool diagonalInput = input.x != 0 && input.y != 0;
		bool keepsHeading = (step.x != 0 && step.x == input.x) || (step.y != 0 && step.y == input.y);
		if (straight && diagonalInput && keepsHeading && elapsed <= window)
		{
			newDest = from + input;
			return Kind.Diagonal;
		}
		return Kind.None;
	}
}

// 칸 이동 중에 눌렀다 뗀 방향키를 칸 도착까지 기억한다(키 씹힘 방지). 이승·멀티 컨트롤러 공용.
// 방향키를 새로 누른 순간의 입력만 기억한다. 키를 떼는 순간(대각선에서 한 키만 먼저 떼기 등)은 기억하지 않아
// 멈추려는 입력이 한 칸 더 가는 입력으로 바뀌지 않는다.
public struct StepInputBuffer
{
	Vector3Int _input;
	bool _has;

	public void Clear() { _has = false; }

	static bool AnyDirKeyDown()
	{
		return Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow)
			|| Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow);
	}

	// 이동 중 매 프레임 호출. Shift(제자리 방향 전환) 중에는 부르지 않는다.
	public void Record(int inputX, int inputY)
	{
		if ((inputX != 0 || inputY != 0) && AnyDirKeyDown())
		{
			_input = new Vector3Int(inputX, inputY, 0);
			_has = true;
		}
	}

	// 칸 도착 시 다음 칸 입력을 정하고 기억을 비운다.
	// 지금 누른 입력이 진행 방향과 다르면(직접 꺾는 중) 그대로 쓰고, 아니면(손을 뗐거나 진행 방향 유지) 기억한 입력을 한 번 쓴다.
	public void Resolve(Vector3Int step, ref int inputX, ref int inputY)
	{
		bool has = _has;
		_has = false;
		Vector3Int held = new Vector3Int(inputX, inputY, 0);
		if (has == false || (held != Vector3Int.zero && held != step))
			return;
		inputX = _input.x;
		inputY = _input.y;
	}
}
