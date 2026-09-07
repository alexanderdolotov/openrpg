extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var player = main.get_node("UI/ChatInput") # placeholder, replaced below
	var bar = main.get_node("UI/NpcFilterBar")
	var log_label = main.get_node("UI/DebugLog")

	# Find the player and one NPC via WorldRegistry by walking _agents
	# is not exposed to GDScript (private) — use WorldRegistry autoload
	# instead, which IS how the rest of the game looks characters up.
	var world_registry = main.get_node_or_null("/root/WorldRegistry")
	print("world_registry: ", world_registry)

	# Fall back: find PlayerCharacter/NPCActor nodes directly under the
	# world layer by class name.
	var world_layer = main.get_node("WorldLayer") if main.has_node("WorldLayer") else null
	print("children of main: ")
	for c in main.get_children():
		print(" - ", c.name, " : ", c.get_class())

	get_tree().quit()
