extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var world_reg = main.get_node("/root/World")
	var debug_log = main.get_node("UI/DebugLog")

	var npc = world_reg.GetEntity("npc_0")
	print("npc_0 found: ", npc != null)

	var wolf = world_reg.GetEntity("animal_4")
	print("animal_4 found: ", wolf != null)
	if wolf:
		print("animal_4 species: ", wolf.get_script().resource_path)

	if npc == null or wolf == null:
		print("SETUP FAILED — aborting")
		quit()
		return

	# Move every rabbit far away first — Wolf.DecideBehavior() checks
	# for a nearby rabbit BEFORE considering a starving attack on a
	# human, regardless of hunger, so a rabbit wandering back into
	# range mid-test would derail this specific test's setup (not a
	# bug — that's correct wolf behavior — just not what this test is
	# isolating).
	for i in range(4):
		var rabbit = world_reg.GetEntity("animal_%d" % i)
		if rabbit:
			rabbit.position = Vector2(5000, 5000)

	# Force a starving wolf right next to the NPC — well within
	# DetectionRadius (260) and AttackRange (36) both — so its own real
	# DecideBehavior() picks "attack this human" on its very next
	# physics tick, no scripted shortcut into the fight itself.
	wolf.Hunger = 5.0
	wolf.position = npc.position + Vector2(20, 0)
	print("wolf teleported to ", wolf.position, " npc at ", npc.position)

	var start_len = debug_log.get_parsed_text().length()
	var last_len = start_len

	# Real time, real physics — watch the console log accumulate for up
	# to ~40 seconds (attack cooldown 1.2s + AttemptDuration 1.5s + a
	# real LLM round trip for DecideThreatResponse all have to actually
	# happen).
	for i in range(40):
		await create_timer(0.5).timeout
		var text = debug_log.get_parsed_text()
		if text.length() != last_len:
			last_len = text.length()
		print("t=", (i + 1) * 0.5, " wolf.Hunger=", wolf.Hunger, " wolf.pos=", wolf.position, " npc.pos=", npc.position, " dist=", wolf.position.distance_to(npc.position))

	var full_log = debug_log.get_parsed_text().substr(start_len)
	print("=== LOG SINCE TEST START ===")
	print(full_log)
	print("=== END LOG ===")

	print("contains 'thinking (danger'? ", full_log.contains("thinking (danger"))
	print("contains 'reflex: attack'? ", full_log.contains("reflex: attack"))
	print("contains 'decides (danger)' or 'random:'? ", full_log.contains("decides (danger)") or full_log.contains("random:"))
	print("contains 'attempting: attack' or 'attempting: flee' or 'attempting: wait'? ", full_log.contains("attempting: attack") or full_log.contains("attempting: flee") or full_log.contains("attempting: wait"))

	quit()
