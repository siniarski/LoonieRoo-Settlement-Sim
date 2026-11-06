// The sim benchmark as a C++ GDExtension: the same BFS floods as bench.gd, BenchCs.cs and the
// TypeScript harness. BenchCpp.bfs_floods(walk, size, starts) runs one flood from each start over
// the walkable tiles (walk[i] == 1) and returns the tiles reached in all.
#include "bench.h"

#include <godot_cpp/core/class_db.hpp>

#include <cstdlib>
#include <queue>
#include <vector>

using namespace godot;

void BenchCpp::_bind_methods() {
	ClassDB::bind_static_method("BenchCpp", D_METHOD("bfs_floods", "walk", "size", "starts"), &BenchCpp::bfs_floods);
	ClassDB::bind_static_method("BenchCpp", D_METHOD("astar_paths", "walk", "size", "pairs"), &BenchCpp::astar_paths);
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

// The same A* as the TypeScript, GDScript and C# copies: 4 ways, Manhattan, a binary heap of
// f * 2^21 + tile with stale entries skipped. Returns the steps of every path found, summed.
int64_t BenchCpp::astar_paths(const PackedByteArray &walk, int64_t size64, const PackedInt32Array &pairs) {
	const int size = static_cast<int>(size64);
	const int n = static_cast<int>(walk.size());
	const uint8_t *w = walk.ptr();
	const int32_t *pr = pairs.ptr();
	const int64_t SHIFT = 2097152;
	std::vector<int> g(n), seen(n, 0);
	std::priority_queue<int64_t, std::vector<int64_t>, std::greater<int64_t>> heap;
	int stamp = 0;
	int64_t total = 0;
	for (int64_t k = 0; k + 1 < pairs.size(); k += 2) {
		const int start = pr[k], goal = pr[k + 1];
		const int gx = goal % size, gy = goal / size;
		auto h = [&](int i) { return std::abs(i % size - gx) + std::abs(i / size - gy); };
		stamp++;
		while (!heap.empty()) heap.pop();
		g[start] = 0;
		seen[start] = stamp;
		heap.push(h(start) * SHIFT + start);
		int found = -1;
		while (!heap.empty()) {
			const int64_t key = heap.top();
			heap.pop();
			const int i = static_cast<int>(key % SHIFT);
			const int64_t f = key / SHIFT;
			if (f > g[i] + h(i)) continue;
			if (i == goal) { found = g[i]; break; }
			const int x = i % size;
			const int ng = g[i] + 1;
			const int around[4] = { x > 0 ? i - 1 : -1, x < size - 1 ? i + 1 : -1, i - size, i + size };
			for (int j : around) {
				if (j < 0 || j >= n || w[j] == 0) continue;
				if (seen[j] == stamp && g[j] <= ng) continue;
				seen[j] = stamp;
				g[j] = ng;
				heap.push((ng + h(j)) * SHIFT + j);
			}
		}
		if (found >= 0) total += found;
	}
	return total;
}
