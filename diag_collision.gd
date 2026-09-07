extends SceneTree

func find_character_bodies(world_layer):
	var bodies = []
	for c in world_layer.get_children():
		if c.get_class() == "CharacterBody2D":
			bodies.append(c)
		elif c.get_class() == "Node":
			for gc in c.get_children():
				if gc.get_class() == "CharacterBody2D":
					bodies.append(gc)
	return bodies

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var world_layer = main.get_node("WorldLayer")
	var bodies = find_character_bodies(world_layer)
	# Spread everyone out so several distant regions get checked in one tick.
	var targets = [Vector2(2600,100), Vector2(2600,-1800), Vector2(-1600,1800), Vector2(2600,1800)]
	for i in range(bodies.size()):
		bodies[i].position = targets[i % targets.size()]

	var found_generated_foothills = false
	for attempt in range(6):
		await create_timer(2.5).timeout
		for c in main.get_children():
			var s = c.get_script()
			if s and s.resource_path.ends_with("Foothills.cs") and c.name != "Foothills":
				found_generated_foothills = true
				var shapes = []
				for gc in c.get_children():
					if gc.get_class() == "CollisionShape2D":
						shapes.append(gc.shape.radius)
				print("generated Foothills '%s' collision shape radii: %s (static_body: %s)" % [c.name, str(shapes), c.get_class()])
		if found_generated_foothills:
			break
		# re-roll: move to fresh far positions each retry since same spot won't regenerate
		for i in range(bodies.size()):
			bodies[i].position = Vector2(bodies[i].position.x + 500, bodies[i].position.y - 300)

	if not found_generated_foothills:
		print("no generated Foothills appeared in 6 attempts (bad luck on the dice roll, not a bug) -- inspect Foothills.cs directly instead")
	quit()
