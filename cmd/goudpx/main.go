package main

import (
	"github.com/firstrow/tcp_server"
	"github.com/yknext/goudpx/pkg/service"
)

func main() {

	server := tcp_server.New("0.0.0.0:6666")

	server.OnNewClient(func(c *tcp_server.Client) {

		udpChan := make(chan []byte, 4096)
		go service.ReadUdpMulticastH264("239.93.0.184:5140", udpChan)
		// new client connected
		// lets send some message
		for {
			err := c.SendBytes(<-udpChan)
			if err != nil {
				return
			}
		}

	})
	server.OnNewMessage(func(c *tcp_server.Client, message string) {
		// new message received
	})
	server.OnClientConnectionClosed(func(c *tcp_server.Client, err error) {
		// connection with client lost
	})

	server.Listen()

	srv := service.NewService()
	srv.Run(":7777")

}
