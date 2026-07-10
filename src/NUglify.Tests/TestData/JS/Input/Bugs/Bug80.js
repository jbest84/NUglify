var o = {
    "a": function (async) {
        async = () => 1;
        async[1] = 2;
        // TODO: this is currently buggy
        //async(b);
    }
};