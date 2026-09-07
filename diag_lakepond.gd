extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame
	await physics_frame

	var lake = main.get_node_or_null("MountainLake")
	print("MountainLake exists: ", lake != null)
	print("MountainLake Radius: ", lake.Radius)

	var sprite = null
	for c in lake.get_children():
		if c is Sprite2D:
			sprite = c
	print("Lake has a Sprite2D child: ", sprite != null)
	if sprite:
		print("Sprite texture assigned: ", sprite.texture != null)
		print("Sprite texture path: ", sprite.texture.resource_path if sprite.texture else "none")
		print("Sprite scale: ", sprite.scale, " (expect Radius/25 = ", lake.Radius / 25.0, ")")

	print("Lake shape owner count (should be 1): ", lake.get_shape_owners().size())

	# Real physics test: standing in the middle of the pond should be blocked.
	var player = main.get_node("WorldLayer/Player")
	var xform = Transform2D(0, lake.position)
	print("player standing in lake center is blocked (should be true): ", player.test_move(xform, Vector2.ZERO))

	# Straight south, well clear of Foothills/MistyMountains which sit
	# to the northeast of this lake and could otherwise confuse the test.
	var outside = lake.position + Vector2(0, lake.Radius * 1.5)
	var xform2 = Transform2D(0, outside)
	var blocked2 = player.test_move(xform2, Vector2.ZERO)
	print("player standing well outside the lake (due south) is blocked (should be false): ", blocked2)
	if blocked2:
		var space_state = player.get_world_2d().direct_space_state
		var query = PhysicsShapeQueryParameters2D.new()
		query.shape = CircleShape2D.new()
		query.shape.radius = 20.0
		query.transform = xform2
		query.collide_with_bodies = true
		for r in space_state.intersect_shape(query, 8):
			print("  overlapping body: ", r["collider"].name, " at ", r["collider"].global_position)

	# One more, far from every hand-placed obstacle, as a clean control.
	var far = lake.position + Vector2(0, -600)
	var xform3 = Transform2D(0, far)
	print("player far from everything is blocked (should be false): ", player.test_move(xform3, Vector2.ZERO))

	quit()
