extends SceneTree

# Real physics test, not a structural check: TestMove asks the physics
# server "if this body moved by `motion` from `transform`, would it
# collide with anything?" -- exactly what MoveAndSlide() relies on
# during actual gameplay.
func test_blocked(body, world_pos_near_obstacle, motion_into_obstacle):
	var xform = Transform2D(0, world_pos_near_obstacle)
	return body.test_move(xform, motion_into_obstacle)

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame
	await physics_frame
	await physics_frame

	var mountains = main.get_node("MistyMountains")
	var foothills = main.get_node("Foothills")
	var player = main.get_node("WorldLayer/Player")

	print("MistyMountains class: ", mountains.get_class())
	var mtn_shapes = 0
	for c in mountains.get_children():
		if c.get_class() == "CollisionShape2D":
			mtn_shapes += 1
	print("MistyMountains collision shapes: ", mtn_shapes)

	# MistyMountains sits at (700,-380) with a peak-base collision circle
	# at local (-40,40) radius 120 -> global (660,-340). Start just
	# outside its edge, moving straight into it.
	var mtn_start = Vector2(660, -340 - 120 - 10)
	var mtn_blocked = test_blocked(player, mtn_start, Vector2(0, 30))
	print("MistyMountains blocks movement into it: ", mtn_blocked)

	# Also confirm walking well clear of any collision circle is NOT
	# blocked, so this isn't a false positive from something unrelated.
	var mtn_clear = test_blocked(player, Vector2(660, -340 - 400), Vector2(0, 10))
	print("MistyMountains does NOT block movement far away (sanity check): ", not mtn_clear)

	# Foothills sits at (820,-220), radius 70, no offset -> collision
	# circle centered exactly at (820,-220).
	var fh_start = Vector2(820, -220 - 70 - 10)
	var fh_blocked = test_blocked(player, fh_start, Vector2(0, 30))
	print("Foothills blocks movement into it: ", fh_blocked)

	quit()
