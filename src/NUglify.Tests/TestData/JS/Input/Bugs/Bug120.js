var o = {};
Object.defineProperty(o, 'a', {
        get() {
             return 'foobar world';
        }
    }
);

Object.defineProperty(o, 'b', {
        set(x) {
            this.lol = x;
        }
    }
);