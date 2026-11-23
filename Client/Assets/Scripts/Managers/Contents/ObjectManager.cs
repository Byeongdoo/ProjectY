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
	const string MyPlayerPrefab = "Creature/MudangMyPlayer";
	const string PlayerPrefab = "Creature/MudangPlayer";

	// 저승 던전에서는 나루터에서 고른 원혼(ObjectInfo.SpiritId)의 캐릭터로 나온다.
	// 프리팹 이름은 spirits.prefabName, 생성은 Tools > ProjectY > Build Spirit Player Prefabs.
	public static string SpiritPrefabPath(Table.SpiritsRow row, bool mine)
	{
		return $"Prefabs/Creature/Spirit/{row.prefabName}{(mine ? "_My" : "")}";
	}

	static string PlayerPrefabFor(int spiritId, bool mine)
	{
		if (spiritId != 0 && Table.TableManager.Spirits.TryGet(spiritId, out Table.SpiritsRow row))
			return SpiritPrefabPath(row, mine).Substring("Prefabs/".Length);
		return mine ? MyPlayerPrefab : PlayerPrefab;
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

	// 투사체 표시 프리팹. 새 리소스가 나올 때까지 비어 있을 수 있다.
	const string ProjectilePrefab = "Creature/Arrow";

	const int ConfigAlliesOverlap = 90005; // 아군끼리 같은 칸 겹침 허용(1=허용)

	// 내 캐릭터가 그 칸으로 들어가지 못하게 막는 대상. 아군 겹침을 허용하면 다른 플레이어는 막지 않는다.
	public GameObject FindBlocker(Vector3Int cellPos)
	{
		bool alliesOverlap = Table.TableManager.Configs.Get(ConfigAlliesOverlap).value[0] >= 1;
		foreach (KeyValuePair<int, GameObject> kv in _objects)
		{
			GameObjectType type = GetObjectTypeById(kv.Key);
			if (type == GameObjectType.Projectile)
				continue;
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
				GameObject go = Managers.Resource.Instantiate(PlayerPrefabFor(info.SpiritId, mine: true));
				go.name = info.Name;
				_objects.Add(info.ObjectId, go);

				MyPlayer = go.GetComponent<MyPlayerController>();
				MyPlayer.Id = info.ObjectId;
				MyPlayer.PosInfo = info.PosInfo;
				MyPlayer.Stat = info.StatInfo;
				MyPlayer.SetStatuses(info.Statuses);
				MyPlayer.SyncPos();
			}
			else
			{
				GameObject go = Managers.Resource.Instantiate(PlayerPrefabFor(info.SpiritId, mine: false));
				go.name = info.Name;
				_objects.Add(info.ObjectId, go);

				PlayerController pc = go.GetComponent<PlayerController>();
				pc.Id = info.ObjectId;
				pc.PosInfo = info.PosInfo;
				pc.Stat = info.StatInfo;
				pc.SetStatuses(info.Statuses);
				pc.SyncPos();
			}
		}
		else if (objectType == GameObjectType.Monster)
		{
			string prefab = MonsterPrefabFor(info.CharacterId);
			if (prefab == null)
			{
				Debug.LogWarning($"망령 프리팹 없음: characterId {info.CharacterId} (Tools > ProjectY > Build Monster Prefabs)");
				return;
			}
			GameObject go = Managers.Resource.Instantiate(prefab);
			go.name = info.Name;
			_objects.Add(info.ObjectId, go);

			MonsterController mc = go.GetComponent<MonsterController>();
			mc.Id = info.ObjectId;
			mc.PosInfo = info.PosInfo;
			mc.Stat = info.StatInfo;
			mc.SetStatuses(info.Statuses);
			mc.SyncPos();
		}
		else if (objectType == GameObjectType.Projectile)
		{
			if (Resources.Load<GameObject>($"Prefabs/{ProjectilePrefab}") == null)
				return; // 투사체 리소스 없음: 서버 판정은 그대로, 클라 표시만 생략
			GameObject go = Managers.Resource.Instantiate(ProjectilePrefab);
			go.name = "Arrow";
			_objects.Add(info.ObjectId, go);

			ArrowController ac = go.GetComponent<ArrowController>();
			ac.PosInfo = info.PosInfo;
			ac.Stat = info.StatInfo;
			ac.SyncPos();
		}
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
