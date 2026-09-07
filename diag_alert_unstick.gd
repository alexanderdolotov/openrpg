extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var world_reg = main.get_node("/root/World")
	var debug_log = main.get_node("UI/DebugLog")

	# Move rabbits away so the wolf doesn't wander off toward one —
	# same reasoning as the earlier combat diagnostics.
	for i in range(4):
		var rabbit = world_reg.GetEntity("animal_%d" % i)
		if rabbit:
			rabbit.position = Vector2(5000, 5000)

	var npc = world_reg.GetEntity("npc_2") # Wren, specifically — the one reported stuck
	var wolf = world_reg.GetEntity("animal_4")
	print("npc_2 found: ", npc != null, " wolf found: ", wolf != null)
	if npc == null or wolf == null:
		print("SETUP FAILED")
		quit()
		return

	# Full (not starving) — won't initiate an attack, just sits there,
	# so this exercises ALERT (sighted, not fighting), not DANGER.
	wolf.Hunger = 90.0
	wolf.position = npc.position + Vector2(150, 0) # within HearingRadius (260) alert range, outside AttackRange
	print("npc at ", npc.position, " wolf at ", wolf.position)

	var last_len = 0
	for i in range(90):
		await create_timer(0.5).timeout

	var full_log = debug_log.get_parsed_text()
	print("=== FULL LOG ===")
	print(full_log)
	print("=== END LOG ===")

	# Count how many separate "Wren ...thinking" cycles happened AFTER
	# any alert reaction — if the bug is present, there'd be at most
	# ONE such line total (whichever came before the freeze). Fixed,
	# there should be several spread across the whole window.
	var think_count = 0
	var idx = 0
	while true:
		idx = full_log.find("[Wren] ...thinking", idx)
		if idx == -1:
			break
		think_count += 1
		idx += 1
	print("Wren thinking-cycle count over 25s: ", think_count, " (should be several, not just 0-1)")
	print("Wren had an alert reaction? ", full_log.contains("[Wren] spots a wolf"))

	quit()
