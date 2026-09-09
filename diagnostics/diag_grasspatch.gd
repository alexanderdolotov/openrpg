extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame
	await physics_frame

	var player = main.get_node("WorldLayer/Player")
	var world_reg = main.get_node("/root/World")
	var grass = world_reg.GetEntity("grass_0")
	print("grass_0 found: ", grass != null)
	if grass == null:
		quit()
		return

	print("grass position: ", grass.position)
	print("grass has a CollisionShape2D child (should be false, Walkable=true): ", grass.get_node_or_null("CollisionShape2D") != null)
	print("grass child count: ", grass.get_child_count())
	for c in grass.get_children():
		print("  child: ", c.name, " (", c.get_class(), ")")
	print("grass shape owner count (should be 0): ", grass.get_shape_owners().size())

	# Real physics test, same tool diag_travel.gd uses to check if a
	# position is actually reachable — walk the player straight through
	# where the grass patch sits and confirm nothing blocks it.
	var through_pos = grass.position
	var xform = Transform2D(0, through_pos)
	var blocked = player.test_move(xform, Vector2.ZERO)
	print("player standing exactly on the grass patch is blocked: ", blocked, " (should be false)")

	# If blocked, find out what's actually overlapping there — could be
	# something else entirely (a tree spawned nearby), not the grass.
	if blocked:
		var space_state = player.get_world_2d().direct_space_state
		var query = PhysicsShapeQueryParameters2D.new()
		query.shape = CircleShape2D.new()
		query.shape.radius = 20.0
		query.transform = xform
		query.collide_with_bodies = true
		var results = space_state.intersect_shape(query, 8)
		for r in results:
			print("overlapping body: ", r["collider"].name, " at ", r["collider"].global_position, " (grass is at ", grass.position, ")")

	quit()
