extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var world_reg = main.get_node("/root/World")
	var debug_log = main.get_node("UI/DebugLog")

	# Move every rabbit far away — same reasoning as diag_fff.gd: a
	# wolf that finds a nearby rabbit will chase it instead of a human,
	# derailing this test's setup (not a bug, just not what's being
	# isolated here).
	for i in range(4):
		var rabbit = world_reg.GetEntity("animal_%d" % i)
		if rabbit:
			rabbit.position = Vector2(5000, 5000)

	var victim = world_reg.GetEntity("npc_0")
	var bystander = world_reg.GetEntity("npc_1")
	var wolf = world_reg.GetEntity("animal_4")
	print("victim(npc_0) found: ", victim != null, " bystander(npc_1) found: ", bystander != null, " wolf found: ", wolf != null)
	if victim == null or bystander == null or wolf == null:
		print("SETUP FAILED")
		quit()
		return

	# Bystander stands nearby (within hearing range) but is NOT the
	# wolf's target — this is what should read as "a friend is in
	# danger" to it, not "I'm in danger."
	bystander.position = victim.position + Vector2(-60, 0)
	wolf.Hunger = 5.0
	wolf.position = victim.position + Vector2(20, 0)
	print("victim at ", victim.position, " bystander at ", bystander.position, " wolf at ", wolf.position)

	for i in range(70):
		await create_timer(0.5).timeout

	var full_log = debug_log.get_parsed_text()
	print("=== FULL LOG (last part) ===")
	print(full_log.substr(max(0, full_log.length() - 6000)))
	print("=== END LOG ===")

	print("Wren/bystander saw 'friend in danger'? ", full_log.contains("friend in danger"))
	print("bystander reflex/decision lines present? ", full_log.contains("decides (danger)") or full_log.contains("random:"))
	print("bystander attempted attack/flee/wait during this? ", full_log.contains("attempting: attack -> animal_4") or full_log.contains("attempting: flee") )

	quit()
