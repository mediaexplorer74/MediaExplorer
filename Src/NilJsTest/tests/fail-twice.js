// Test that throwing twice causes a persisted bad-hash when --persist is used
throw new Error('synthetic-fail-1');
throw new Error('synthetic-fail-1');
