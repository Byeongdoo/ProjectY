using Google.Protobuf.Protocol;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ObjectManager
{
	public MyPlayerController MyPlayer { get; set; }
	Dictionary<int, GameObject> _objects = new Dictionary<int, GameObject>();

	// 무당 본모습(주인공 스프라이트, 삼천도). 프리팹은 멀티 씬 빌더(Tools > ProjectY > Levels > Build Multi Prototype)가 만든다.
	// 원혼과 같이 외형만 담은 프리팹 1개이고, 컨트롤러는 생성할 때 붙인다(InstantiatePlayer).
	const string MudangPlayerPrefab = "Creature/MudangPlayer";

	// 저승 던전에서는 나루터에서 고른 원혼(ObjectInfo.SpiritId)의 캐릭터로 나온다.
	// 프리팹 이름은 spirits.prefabName, 생성은 Tools > ProjectY > Build Spirit Player Prefabs.
	// 원혼 프리팹은 원혼당 1개(외형 + CreatureVisual)이고, 컨트롤러는 생성할 때 붙인다(InstantiatePlayer).
	public static string SpiritPrefabPath(Table.SpiritsRow row)
	{
		return $"Prefabs/Creature/Spirit/{row.prefabName}";
	}

	static string PlayerPrefabFor(int spiritId)
	{
		if (spiritId != 0 && Table.TableManager.Spirits.TryGet(spiritId, out Table.SpiritsRow row))
			return SpiritPrefabPath(row).Substring("Prefabs/".Length);
		return MudangPlayerPrefab;
	}

	// 플레이어 프리팹(무당·원혼)은 외형만 담는다. 내 캐릭터면 MyPlayerController, 다른 플레이어면 PlayerController를 붙인다.
	// Init은 Start에서 돌므로 여기서 붙여도 같은 프레임 안에 초기화된다.
	static T InstantiatePlayer<T>(int spiritId) where T : PlayerController
	{
		GameObject go = Managers.Resource.Instantiate(PlayerPrefabFor(spiritId));
		return go.AddComponent<T>();
	}

	// 망령은 characterInfos.prefabName 프리팹(Prefabs/Creature/Monster, 생성은 Tools > ProjectY > Build Monster Prefabs).
	// characterId가 0이거나 프리팹이 아직 없으면 null(표시하지 않음).
	static string MonsterPrefabFor(int characterId)
	{
		if (characterId != 0 && Table.TableManager.CharacterInfos.TryGet(characterId, out Table.CharacterInfosRow row))
		{
			string path = $"Creature/Monster/{row.prefabName}";
			if (Resources.Load<GameObject>($"Prefabs/{path}") != null)
				return path;
		}
		return null;
	}

	const int ConfigAlliesOverlap = 90005; // 아군끼리 같은 칸 겹침 허용(1=허용)

	// 내 캐릭터가 그 칸으로 들어가지 못하게 막는 대상. 아군 겹침을 허용하면 다른 플레이어는 막지 않는다.
	public GameObject FindBlocker(Vector3Int cellPos)
	{
		bool alliesOverlap = Table.TableManager.Configs.Get(ConfigAlliesOverlap).value[0] >= 1;
		foreach (KeyValuePair<int, GameObject> kv in _objects)
		{
			GameObjectType type = GetObjectTypeById(kv.Key);
			if (type == GameObjectType.Field)
				continue; // 설치물(덫)은 칸을 막지 않는다
			if (alliesOverlap && type == GameObjectType.Player)
				continue;

			CreatureController cc = kv.Value.GetComponent<CreatureController>();
			if (cc != null && cc.CellPos == cellPos)
				return kv.Value;
		}

		return null;
	}

	public static GameObjectType GetObjectTypeById(int id)
	{
		int type = (id >> 24) & 0x7F;
		return (GameObjectType)type;
	}

	public void Add(ObjectInfo info, bool myPlayer = false)
	{
		GameObjectType objectType = GetObjectTypeById(info.ObjectId);
		if (objectType == GameObjectType.Player)
		{
			if (myPlayer)
			{
				MyPlayer = InstantiatePlayer<MyPlayerController>(info.SpiritId);
				GameObject go = MyPlayer.gameObject;
				go.name = info.Name;
				_objects.Add(info.ObjectId, go);

				MyPlayer.Id = info.ObjectId;
				MyPlayer.SpiritId = info.SpiritId;
				MyPlayer.PosInfo = info.PosInfo;
				MyPlayer.Stat = info.StatInfo;
				MyPlayer.SyncPos();
				MyCombat.Reset(info.StatInfo.Spirit, info.StatInfo.MaxSpirit);
				TargetSelect.End();
			}
			else
			{
				PlayerController pc = InstantiatePlayer<PlayerController>(info.SpiritId);
				GameObject go = pc.gameObject;
				go.name = info.Name;
				_objects.Add(info.ObjectId, go);

				pc.Id = info.ObjectId;
				pc.SpiritId = info.SpiritId;
				pc.PosInfo = info.PosInfo;
				pc.Stat = info.StatInfo;
				pc.SyncPos();
			}
		}
		else if (objectType == GameObjectType.Monster)
		{
			// 개발·테스트용 허수아비는 테이블 프리팹 없이 플레이스홀더로
			string prefab = info.TrainingDummy ? null : MonsterPrefabFor(info.CharacterId);
			if (prefab == null && info.TrainingDummy == false)
			{
				Debug.LogWarning($"망령 프리팹 없음: characterId {info.CharacterId} (Tools > ProjectY > Build Monster Prefabs)");
				return;
			}
			GameObject go = info.TrainingDummy ? TrainingDummyView.Create(info) : Managers.Resource.Instantiate(prefab);
			go.name = info.Name;
			_objects.Add(info.ObjectId, go);

			MonsterController mc = go.GetComponent<MonsterController>();
			mc.Id = info.ObjectId;
			mc.PosInfo = info.PosInfo;
			mc.Stat = info.StatInfo;
			mc.SyncPos();
		}
		else if (objectType == GameObjectType.Field)
		{
			// 설치물(덫). 리소스가 나올 때까지 CombatHud가 칸 표시로 대신 그린다
			GameObject go = new GameObject($"Field_{info.SkillId}");
			FieldController fc = go.AddComponent<FieldController>();
			fc.Id = info.ObjectId;
			fc.SkillId = info.SkillId;
			fc.CellPos = new Vector3Int(info.PosInfo.PosX, info.PosInfo.PosY, 0);
			go.transform.position = Managers.Map.CurrentGrid.CellToWorld(fc.CellPos) + new Vector3(0.5f, 0.5f);
			_objects.Add(info.ObjectId, go);
		}
	}

	// 칸 거리(체비셰프) range 안의 살아 있는(fainted면 혼절한) 플레이어·망령. 대상 선택 후보
	public List<CreatureController> FindCreatures(GameObjectType type, Vector3Int center, int range, bool fainted = false)
	{
		List<CreatureController> list = new List<CreatureController>();
		foreach (KeyValuePair<int, GameObject> kv in _objects)
		{
			if (GetObjectTypeById(kv.Key) != type)
				continue;
			CreatureController cc = kv.Value.GetComponent<CreatureController>();
			if (cc == null)
				continue;
			bool down = cc.State == CreatureState.Dead || cc.Hp <= 0;
			if (down != fainted)
				continue;
			Vector3Int d = cc.CellPos - center;
			if (Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y)) <= range)
				list.Add(cc);
		}
		return list;
	}

	public IEnumerable<FieldController> Fields()
	{
		foreach (KeyValuePair<int, GameObject> kv in _objects)
			if (GetObjectTypeById(kv.Key) == GameObjectType.Field)
				yield return kv.Value.GetComponent<FieldController>();
	}

	public void Remove(int id)
	{
		GameObject go = FindById(id);
		if (go == null)
			return;

		_objects.Remove(id);
		Managers.Resource.Destroy(go);
	}

	public GameObject FindById(int id)
	{
		GameObject go = null;
		_objects.TryGetValue(id, out go);
		return go;
	}

	public GameObject FindCreature(Vector3Int cellPos)
	{
		foreach (GameObject obj in _objects.Values)
		{
			CreatureController cc = obj.GetComponent<CreatureController>();
			if (cc == null)
				continue;

			if (cc.CellPos == cellPos)
				return obj;
		}

		return null;
	}

	public GameObject Find(Func<GameObject, bool> condition)
	{
		foreach (GameObject obj in _objects.Values)
		{
			if (condition.Invoke(obj))
				return obj;
		}

		return null;
	}

	public void Clear()
	{
		foreach (GameObject obj in _objects.Values)
			Managers.Resource.Destroy(obj);
		_objects.Clear();
		MyPlayer = null;
	}
}
