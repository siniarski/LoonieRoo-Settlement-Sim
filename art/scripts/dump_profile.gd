extends SceneTree
func _init():
	var p := SkeletonProfileHumanoid.new()
	var out := []
	for i in p.bone_size:
		var t: Transform3D = p.get_reference_pose(i)
		out.append({"name": String(p.get_bone_name(i)), "parent": String(p.get_bone_parent(i)), "tail": String(p.get_bone_tail(i)), "group": String(p.get_group(i)),
			"basis": [t.basis.x.x, t.basis.x.y, t.basis.x.z, t.basis.y.x, t.basis.y.y, t.basis.y.z, t.basis.z.x, t.basis.z.y, t.basis.z.z],
			"origin": [t.origin.x, t.origin.y, t.origin.z]})
	var f := FileAccess.open(OS.get_cmdline_user_args()[0], FileAccess.WRITE)
	f.store_string(JSON.stringify({"root": String(p.root_bone), "scale_base": String(p.scale_base_bone), "bones": out}, "  "))
	f.close()
	print("bones: ", p.bone_size)
	quit()
