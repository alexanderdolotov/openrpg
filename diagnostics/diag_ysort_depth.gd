extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var world_layer = main.get_node("WorldLayer")
	print("WorldLayer direct children:")
	for c in world_layer.get_children():
		print("  ", c.name, " : ", c.get_class(), " script=", (c.get_script().resource_path.get_file() if c.get_script() else "none"))

	# The actual claim to verify: every NPCActor (CharacterBody2D) is now
	# a DIRECT child of WorldLayer, same depth as Player and every
	# AppleTree -- not nested one level deeper under an NpcAgent wrapper.
	var all_direct_bodies_and_trees_same_depth = true
	var character_count = 0
	for c in world_layer.get_children():
		var s = c.get_script()
		if s and s.resource_path.ends_with("NPCActor.cs"):
			character_count += 1
			print("NPCActor '%s' is a DIRECT WorldLayer child: true (class=%s)" % [c.name, c.get_class()])
		elif s and s.resource_path.ends_with("PlayerCharacter.cs"):
			character_count += 1
			print("PlayerCharacter '%s' is a DIRECT WorldLayer child: true" % c.name)
		elif c.get_class() == "Node":
			# NpcAgent should now be a plain sibling with NO CharacterBody2D child.
			for gc in c.get_children():
				if gc.get_class() == "CharacterBody2D":
					all_direct_bodies_and_trees_same_depth = false
					print("BAD: found a CharacterBody2D ('%s') still nested under a wrapper Node ('%s')" % [gc.name, c.name])

	print("")
	print("character bodies found as direct WorldLayer children: ", character_count, " (expect 4: 3 NPCs + player)")
	print("no character bodies remain nested under a wrapper: ", all_direct_bodies_and_trees_same_depth)

	# Sanity: NpcAgent.Actor reference still resolves correctly (it's a
	# public Godot-exposed property) despite no longer being its parent.
	for c in world_layer.get_children():
		var s = c.get_script()
		if s and s.resource_path.ends_with("NpcAgent.cs"):
			var actor = c.get("Actor")
			print("NpcAgent '%s'.Actor resolves to: %s (name=%s)" % [c.name, actor, (actor.name if actor else "null")])

	quit()
