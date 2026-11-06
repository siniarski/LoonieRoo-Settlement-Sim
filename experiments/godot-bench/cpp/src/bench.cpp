// The sim benchmark as a C++ GDExtension: the same BFS floods as bench.gd, BenchCs.cs and the
// TypeScript harness. BenchCpp.bfs_floods(walk, size, starts) runs one flood from each start over
// the walkable tiles (walk[i] == 1) and returns the tiles reached in all.
#include "bench.h"

#include <godot_cpp/core/class_db.hpp>

#include <vector>

using namespace godot;

void BenchCpp::_bind_methods() {
	ClassDB::bind_static_method("BenchCpp", D_METHOD("bfs_floods", "walk", "size", "starts"), &BenchCpp::bfs_floods);
}

int64_t BenchCpp::bfs_floods(const PackedByteArray &walk, int64_t size, const PackedInt32Array &starts) {
	const int n = static_cast<int>(walk.size());
	const uint8_t *w = walk.ptr();
	std::vector<int> dist(n);
	std::vector<int> queue(n);
	int64_t reached = 0;
	for (int k = 0; k < starts.size(); k++) {
		std::fill(dist.begin(), dist.end(), -1);
		int head = 0, tail = 0;
		const int start = starts[k];
		queue[tail++] = start;
		dist[start] = 0;
		while (head < tail) {
			const int c = queue[head++];
			const int x = c % static_cast<int>(size);
			const int d = dist[c] + 1;
			const int around[4] = { x > 0 ? c - 1 : -1, x < size - 1 ? c + 1 : -1, c >= size ? c - static_cast<int>(size) : -1, c + size < n ? c + static_cast<int>(size) : -1 };
			for (int j : around) {
				if (j < 0 || dist[j] != -1 || w[j] == 0) continue;
				dist[j] = d;
				queue[tail++] = j;
			}
		}
		reached += tail;
	}
	return reached;
}
