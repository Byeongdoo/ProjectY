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
